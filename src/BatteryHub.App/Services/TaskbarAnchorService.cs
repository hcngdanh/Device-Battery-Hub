using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BatteryHub.App.Interop;

namespace BatteryHub.App.Services;

/// <summary>
/// Keeps the BatteryHub overlay window anchored immediately to the left of the
/// Windows 11 System Tray chevron (TrayNotifyWnd) on Shell_TrayWnd, hiding automatically
/// during fullscreen apps or when the taskbar auto-hides.
/// </summary>
public sealed class TaskbarAnchorService : IDisposable
{
    private readonly Window _overlay;
    private readonly DispatcherTimer _syncTimer;
    private readonly TaskbarNative.WinEventDelegate _winEventDelegate;
    private IntPtr _foregroundHook;
    private bool _enabled = true;
    private bool _hasContent = true;
    private TaskbarNative.RECT _lastPlacedRect;

    public TaskbarAnchorService(Window overlay)
    {
        _overlay = overlay;
        _syncTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _syncTimer.Tick += (_, _) => Reposition();
        _winEventDelegate = OnWinEvent;
    }

    public void Start()
    {
        ApplyExtendedStyles();
        if (_foregroundHook == IntPtr.Zero)
        {
            _foregroundHook = TaskbarNative.SetWinEventHook(
                TaskbarNative.EVENT_SYSTEM_FOREGROUND,
                TaskbarNative.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                TaskbarNative.WINEVENT_OUTOFCONTEXT);
        }
        _syncTimer.Start();
        Reposition(force: true);
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        _overlay.Dispatcher.BeginInvoke(() => Reposition(force: true), DispatcherPriority.Send);
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        Reposition();
    }

    public void SetHasContent(bool hasContent)
    {
        _hasContent = hasContent;
        Reposition();
    }

    public void ApplyExtendedStyles()
    {
        var hwnd = new WindowInteropHelper(_overlay).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        nint exStyle = TaskbarNative.GetWindowLongPtr(hwnd, TaskbarNative.GWL_EXSTYLE);
        exStyle |= TaskbarNative.WS_EX_TOOLWINDOW | TaskbarNative.WS_EX_NOACTIVATE;
        TaskbarNative.SetWindowLongPtr(hwnd, TaskbarNative.GWL_EXSTYLE, exStyle);
    }

    internal bool TryGetAnchorPixelRect(out TaskbarNative.RECT overlayRect)
    {
        overlayRect = default;
        var hwnd = new WindowInteropHelper(_overlay).Handle;
        if (hwnd == IntPtr.Zero)
            return false;

        IntPtr taskbarHwnd = TaskbarNative.FindWindow("Shell_TrayWnd", null);
        if (taskbarHwnd == IntPtr.Zero || !TaskbarNative.IsWindowVisible(taskbarHwnd))
            return false;

        if (!TaskbarNative.GetWindowRect(taskbarHwnd, out var taskbarRect) ||
            taskbarRect.Width <= 0 || taskbarRect.Height <= 0)
            return false;

        // Check if taskbar is auto-hidden below screen edge
        var screenHeight = System.Windows.Forms.Screen.FromHandle(taskbarHwnd).Bounds.Bottom;
        if (taskbarRect.Top >= screenHeight - 3)
            return false;

        IntPtr notifyHwnd = TaskbarNative.FindWindowEx(taskbarHwnd, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notifyHwnd == IntPtr.Zero || !TaskbarNative.GetWindowRect(notifyHwnd, out var notifyRect) ||
            notifyRect.Width <= 0)
            return false;

        var dpi = VisualTreeHelper.GetDpi(_overlay);
        double scaleX = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
        double scaleY = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;

        _overlay.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        double desiredWidthDip = Math.Max(_overlay.DesiredSize.Width, _overlay.ActualWidth);
        double desiredHeightDip = Math.Max(_overlay.DesiredSize.Height, 28);

        int widthPx = Math.Max(1, (int)Math.Round(desiredWidthDip * scaleX));
        int heightPx = Math.Min(notifyRect.Height, Math.Max(20, (int)Math.Round(desiredHeightDip * scaleY)));
        int gapPx = (int)Math.Round(4 * scaleX);
        // Windows 11 taskbar includes a 1px top border, so tray icons sit ~1 DIP lower than geometric center.
        int opticalOffsetPx = (int)Math.Round(1.5 * scaleY);

        int rightPx = notifyRect.Left - gapPx;
        int leftPx = Math.Max(taskbarRect.Left + 8, rightPx - widthPx);
        int topPx = notifyRect.Top + (notifyRect.Height - heightPx) / 2 + opticalOffsetPx;

        overlayRect = new TaskbarNative.RECT
        {
            Left = leftPx,
            Top = topPx,
            Right = leftPx + widthPx,
            Bottom = topPx + heightPx
        };
        return true;
    }

    public void Reposition(bool force = false)
    {
        var hwnd = new WindowInteropHelper(_overlay).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        if (!_enabled || !_hasContent || IsFullscreenActive())
        {
            if (_overlay.IsVisible)
                _overlay.Hide();
            return;
        }

        if (!TryGetAnchorPixelRect(out var rect))
        {
            if (_overlay.IsVisible)
                _overlay.Hide();
            return;
        }

        if (!_overlay.IsVisible)
        {
            _overlay.Show();
            ApplyExtendedStyles();
            force = true;
        }

        IntPtr taskbarHwnd = TaskbarNative.FindWindow("Shell_TrayWnd", null);
        bool zOrderLost = taskbarHwnd != IntPtr.Zero && !IsAboveTaskbar(hwnd, taskbarHwnd);

        if (!force &&
            !zOrderLost &&
            rect.Left == _lastPlacedRect.Left &&
            rect.Top == _lastPlacedRect.Top &&
            rect.Right == _lastPlacedRect.Right &&
            rect.Bottom == _lastPlacedRect.Bottom)
        {
            return;
        }

        _lastPlacedRect = rect;
        TaskbarNative.SetWindowPos(
            hwnd,
            TaskbarNative.HWND_TOPMOST,
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height,
            TaskbarNative.SWP_NOACTIVATE | TaskbarNative.SWP_SHOWWINDOW);
    }

    private static bool IsAboveTaskbar(IntPtr overlayHwnd, IntPtr taskbarHwnd)
    {
        IntPtr cursor = taskbarHwnd;
        for (int i = 0; i < 256; i++)
        {
            cursor = TaskbarNative.GetWindow(cursor, TaskbarNative.GW_HWNDPREV);
            if (cursor == IntPtr.Zero)
                return false;
            if (cursor == overlayHwnd)
                return true;
        }
        return false;
    }

    private static bool IsFullscreenActive()
    {
        if (TaskbarNative.SHQueryUserNotificationState(out var state) != 0)
            return false;

        return state is TaskbarNative.UserNotificationState.Busy
            or TaskbarNative.UserNotificationState.RunningD3DFullScreen
            or TaskbarNative.UserNotificationState.PresentationMode;
    }

    public void Dispose()
    {
        _syncTimer.Stop();
        if (_foregroundHook != IntPtr.Zero)
        {
            TaskbarNative.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
    }
}
