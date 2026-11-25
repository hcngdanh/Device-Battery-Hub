using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using BatteryHub.App.Interop;
using BatteryHub.App.ViewModels;

namespace BatteryHub.App.Views;

public partial class TaskbarOverlayWindow : Window
{
    private System.Windows.Media.Brush _hoverBrush = CreateBrush(25, 255, 255, 255);

    public System.Windows.Media.Brush HoverBrush
    {
        get => _hoverBrush;
        private set => _hoverBrush = value;
    }

    public event Action? LeftClicked;
    public event Action? RightClicked;
    public event Action? DeviceChangeDetected;

    public TaskbarOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => LayoutUpdatedOnce?.Invoke();
    }

    public event Action? LayoutUpdatedOnce;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == TaskbarNative.WM_DEVICECHANGE)
        {
            DeviceChangeDetected?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void UpdateDevices(IReadOnlyList<DeviceDisplayItem> items, bool isLightTaskbar)
    {
        HoverBrush = isLightTaskbar
            ? CreateBrush(20, 0, 0, 0)
            : CreateBrush(28, 255, 255, 255);

        DevicesItemsControl.ItemsSource = items;
        UpdateLayout();
        LayoutUpdatedOnce?.Invoke();
    }

    private void OnBarMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        LeftClicked?.Invoke();
    }

    private void OnBarMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        RightClicked?.Invoke();
    }

    private static System.Windows.Media.Brush CreateBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
