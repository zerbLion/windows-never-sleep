# Building

## Requirements

- Windows 10 or Windows 11
- Windows PowerShell 5.1 or newer
- .NET Framework 4.x C# compiler (included with supported Windows installations)

## Build

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Release.ps1
```

The script creates:

- `dist\WindowsNeverSleep-1.0.0\Install.exe`
- `dist\WindowsNeverSleep-1.0.0\Uninstall.exe`
- `dist\WindowsNeverSleep-1.0.0.zip`
- SHA-256 checksums for both executables

`Install.exe` embeds the runtime and uninstaller as managed resources, so end users only need the two visible executable entry points.
