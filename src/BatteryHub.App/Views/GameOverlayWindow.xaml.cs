using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BatteryHub.App.Interop;
using BatteryHub.App.ViewModels;
using BatteryHub.Core.Models;

namespace BatteryHub.App.Views;

/// <summary>Click-through battery readout pinned to a screen corner while gaming.</summary>
public partial class GameOverlayWindow : Window
{
    public GameOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED));
        };
        SizeChanged += (_, _) => Reposition();
    }

    public OverlayCorner Corner { get; private set; } = OverlayCorner.TopRight;

    public void Update(IReadOnlyList<DeviceDisplayItem> items, OverlayCorner corner, double opacity)
    {
        Items.ItemsSource = items;
        Corner = corner;
        Card.Opacity = opacity;
        Card.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Reposition();
    }

    public void Reposition()
    {
        if (!IsLoaded)
            return;

        // Monitor of the foreground (game) window, converted from physical pixels to DIPs.
        var (l, t, r, b) = ShellState.GetActiveMonitorBounds();
        var dpi = VisualTreeHelper.GetDpi(this);
        double left = l / dpi.DpiScaleX, top = t / dpi.DpiScaleY, right = r / dpi.DpiScaleX, bottom = b / dpi.DpiScaleY;

        Left = Corner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft ? left : right - ActualWidth;
        Top = Corner is OverlayCorner.TopLeft or OverlayCorner.TopRight ? top : bottom - ActualHeight;
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
