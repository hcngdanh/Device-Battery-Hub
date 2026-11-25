using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace BatteryHub.App.Views;

/// <summary>
/// Self-drawn notification: a Windows 11 style card above the taskbar, or a small click-through
/// pill at the top of the screen for game mode. Never takes focus.
/// </summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _hideTimer = new();
    private Action? _onClick;
    private bool _compact;

    public ToastWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) => FadeOut();
        SourceInitialized += (_, _) => ApplyExtendedStyle();
    }

    public void ShowFull(string title, string body, Geometry? icon, string? glyph, System.Windows.Media.Brush color, TimeSpan duration, Action? onClick = null)
    {
        _compact = false;
        _onClick = onClick;
        FullCard.Visibility = Visibility.Visible;
        CompactCard.Visibility = Visibility.Collapsed;
        FullTitle.Text = title;
        FullBody.Text = body;
        FullBody.Visibility = string.IsNullOrEmpty(body) ? Visibility.Collapsed : Visibility.Visible;
        SetIcon(FullPath, FullIconPath, FullIconGlyph, icon, glyph, color);
        Present(duration);
    }

    public void ShowCompact(string text, Geometry? icon, string? glyph, System.Windows.Media.Brush color, TimeSpan duration)
    {
        _compact = true;
        _onClick = null;
        FullCard.Visibility = Visibility.Collapsed;
        CompactCard.Visibility = Visibility.Visible;
        CompactText.Text = text;
        CompactPath.Data = icon;
        CompactPath.Stroke = color;
        CompactPath.Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
        ((FrameworkElement)CompactPath.Parent).Visibility = CompactPath.Visibility;
        CompactGlyph.Text = glyph ?? string.Empty;
        CompactGlyph.Visibility = icon is null && glyph is not null ? Visibility.Visible : Visibility.Collapsed;
        Present(duration);
    }

    private static void SetIcon(System.Windows.Shapes.Path path, FrameworkElement pathHost, System.Windows.Controls.TextBlock glyphText,
        Geometry? icon, string? glyph, System.Windows.Media.Brush color)
    {
        path.Data = icon;
        path.Stroke = color;
        pathHost.Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
        glyphText.Text = glyph ?? string.Empty;
        glyphText.Foreground = color;
        glyphText.Visibility = icon is null && glyph is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Present(TimeSpan duration)
    {
        _hideTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        if (!IsVisible)
            Show();
        ApplyExtendedStyle();
        UpdateLayout();
        Reposition();

        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        _hideTimer.Interval = duration;
        _hideTimer.Start();
    }

    private void Reposition()
    {
        if (_compact)
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + 16;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth;
            Top = area.Bottom - ActualHeight;
        }
    }

    private void FadeOut()
    {
        _hideTimer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (Opacity == 0)
                Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void OnCardClicked(object sender, MouseButtonEventArgs e)
    {
        var action = _onClick;
        FadeOut();
        action?.Invoke();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        FadeOut();
    }

    private void ApplyExtendedStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        // The compact pill must never intercept clicks meant for the game underneath.
        style = _compact ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
