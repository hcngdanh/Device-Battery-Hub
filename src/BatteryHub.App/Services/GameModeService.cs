using System.Windows.Threading;
using BatteryHub.App.Interop;

namespace BatteryHub.App.Services;

/// <summary>
/// Game mode is active when the user turned it on from the tray, or when auto-enable is on and a
/// full-screen app owns the screen. Polls cheaply (no device I/O) and raises <see cref="Changed"/>.
/// </summary>
public sealed class GameModeService : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly Func<bool> _autoEnabled;
    private bool _fullscreen;
    private bool _manual;
    private bool _lastActive;

    public GameModeService(Func<bool> autoEnabled)
    {
        _autoEnabled = autoEnabled;
        _timer.Tick += (_, _) => Poll();
    }

    public bool IsActive => _manual || (_autoEnabled() && _fullscreen);
    public bool IsManual => _manual;

    public event Action<bool>? Changed;

    public void Start()
    {
        _timer.Start();
        Poll();
    }

    public void SetManual(bool on)
    {
        _manual = on;
        Evaluate();
    }

    /// <summary>Re-check after settings change (auto-enable toggled).</summary>
    public void Evaluate()
    {
        bool active = IsActive;
        if (active != _lastActive)
        {
            _lastActive = active;
            Changed?.Invoke(active);
        }
    }

    private void Poll()
    {
        _fullscreen = ShellState.IsFullscreenAppActive();
        Evaluate();
    }

    public void Dispose() => _timer.Stop();
}
