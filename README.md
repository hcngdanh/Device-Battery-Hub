# Battery Hub

Lightweight Windows 11 taskbar widget that shows the battery level of your Bluetooth and 2.4GHz devices (headphones, mouse, keyboard, gamepad, speaker) right next to the system tray.

## Features

- **Taskbar readout** — device icon plus percentage, a Windows-style battery glyph, or both; optional green / yellow / red colouring.
- **Flyout** — all devices with live levels, charging state and connection status; connected devices first.
- **Notifications** — low battery, very low battery (repeats every X minutes), fully charged (100% or a custom target), charger plugged / unplugged, device connected / disconnected, end-of-day charge reminder. Optional sound; respects Windows *Do not disturb*.
- **Game mode** — turns on automatically for full-screen apps; notifications become a small pill or are muted; optional click-through battery overlay in a screen corner with a global show / hide shortcut.
- **Battery history** — optional local log (7 / 30 / 90 days) with a chart per device.
- **Settings** — English / Tiếng Việt, System / Light / Dark theme, export / import / restore defaults.
- **Updates** — checks GitHub Releases and updates itself in place.
- **Portable mode** — put an (even empty `{}`) `BatteryHub.settings.json` next to the exe and settings + history are stored there instead of `%LOCALAPPDATA%\BatteryHub`.

Supported battery sources: Windows Bluetooth battery reporting (`DEVPKEY_Bluetooth_Battery`), BLE GATT Battery Service (`0x180F`/`0x2A19`) and Attack Shark X11 2.4GHz receivers (HID).

## Project structure

- `src/BatteryHub.Core` — battery providers, device classifier, `BatteryMonitorService`, `NotificationEngine`, `BatteryHistoryService`, settings persistence and Windows startup integration.
- `src/BatteryHub.App` — WPF UI: taskbar overlay, flyout, Settings window, toasts, game overlay, tray icon, localization (`Localization/Loc.cs`), theme (`Themes/Controls.xaml`, `Services/ThemeManager.cs`) and the GitHub updater.
- `tests/BatteryHub.Core.Tests` — unit tests (classification, settings, notifications, history, change detection).

## Build & run

```powershell
# Build
dotnet build BatteryHub.sln -c Release

# Unit tests
dotnet run --project tests\BatteryHub.Core.Tests

# Run
dotnet run --project src\BatteryHub.App
```

## Releasing

```powershell
# Portable single-file builds for x64 and ARM64 (+ SHA-256) in dist\v<version>\
.\scripts\publish.ps1
```

Create a GitHub release tagged `v<version>` and attach the `BatteryHub-<version>-win-*.exe` files with their `.sha256`. Installed copies pick the update up from *Settings → About & updates*.

## License

[MIT](LICENSE)
