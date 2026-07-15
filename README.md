# 💤 Windows Never Sleep

[English](README.md) | [简体中文](README.zh-CN.md)

> Keep Windows awake and prevent idle locking—with just two clicks.

A tiny open-source Windows utility with two simple buttons:

- **Install.exe** — installs, starts immediately, prevents idle sleep and idle locking, and starts automatically at sign-in.
- **Uninstall.exe** — stops the utility, removes auto-start, restores the previous sleep timeout, and uninstalls it.

手动按 `Win + L` 仍然可以正常锁屏。

## 🚀 Download

Download the latest ZIP from [GitHub Releases](https://github.com/zerbLion/windows-never-sleep/releases/latest), extract it, and double-click `Install.exe`.

To remove it, double-click `Uninstall.exe` or uninstall **Windows Never Sleep** from Windows Settings → Apps → Installed apps.

## ✨ What it does

- Uses native Windows power requests to prevent idle sleep.
- Sets the active power plan's AC/DC idle sleep timeout to `Never` and remembers the previous values.
- Sends a non-text `F15` input pulse after 8 minutes of inactivity so a 10-minute idle-lock policy does not trigger.
- Preserves manual lock, sleep, shutdown, and the power button.
- Installs per-user under `%LOCALAPPDATA%\WindowsNeverSleep`; administrator rights are not required in the normal case.

The F15 pulse also resets Windows' software display-off timer. If you need a dark screen while unattended GUI automation continues, turn off the monitor using its physical power button.

## 📦 Two-button experience

| Button | Result |
|---|---|
| `Install.exe` | Installs, starts immediately, and enables auto-start at sign-in |
| `Uninstall.exe` | Stops, removes, and restores the previous sleep timeout |

## 🛠️ Build from source

On Windows PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Release.ps1
```

See [Building](docs/BUILDING.md) and [Technical details](docs/TECHNICAL.md).

## 📄 License

[MIT](LICENSE)
