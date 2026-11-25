# BatteryHub

Lightweight Windows 11 WPF taskbar widget that displays Bluetooth device battery percentages (headphones, mouse, keyboard, gamepad, speaker) right next to the System Tray chevron (`^`).

## Project Structure

- `src/BatteryHub.Core` — Bluetooth battery providers (`WindowsPnPBatteryProvider` using `DEVPKEY_Bluetooth_Battery` + BLE GATT `0x180F`/`0x2A19` fallback, plus `AttackSharkHidBatteryProvider` for 2.4GHz receivers), device classifier, settings persistence, and Windows startup integration.
- `src/BatteryHub.App` — WPF taskbar overlay (`TaskbarOverlayWindow`), Windows 11 anchor service (`TaskbarAnchorService`), detail flyout (`FlyoutWindow`), and companion system tray icon (`TrayIconService`).
- Settings (flyout → **Settings**): taskbar display mode (Icon + % / % only / Icon only), low-battery threshold, color-coding by level (green > 50%, yellow threshold–50%, red < threshold), and toggles for low-battery, fully-charged and plug/unplug notifications. Devices are polled every 1s, plus instant refresh on device arrival/removal and HID battery pushes.
- `tests/BatteryHub.Core.Tests` — Unit tests for classification, settings persistence, and battery monitor alert hysteresis.

## Build & Run

```powershell
# Build entire solution
dotnet build BatteryHub.sln -c Release

# Run unit tests
dotnet run --project tests\BatteryHub.Core.Tests

# Run live (reads connected Windows Bluetooth devices)
dotnet run --project src\BatteryHub.App
```

## License

[MIT](LICENSE)
