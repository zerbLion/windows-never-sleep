# Technical details

Windows Never Sleep consists of three C# programs:

- `WindowsNeverSleep.exe` is embedded inside `Install.exe` and runs silently in the current user session.
- `Install.exe` extracts the runtime, stores rollback state, changes the active sleep timeout, registers login auto-start, and adds an Installed Apps entry.
- `Uninstall.exe` stops the runtime, restores the saved AC/DC timeout, removes registry entries, and deletes the installed files.

## Power behavior

The runtime holds a `PowerRequestSystemRequired` request through `PowerCreateRequest` and also calls `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)` as a fallback. The active power plan's `STANDBYIDLE` AC/DC values are set to zero during installation and restored during uninstall.

## Idle-lock behavior

Every 15 seconds the runtime reads `GetLastInputInfo`. At 480 seconds of inactivity it sends an `F15` key-down/key-up pair through `keybd_event`. F15 produces no text but resets the Windows input-idle clock before a 600-second lock policy fires.

The pulse cannot unlock the Windows secure desktop. Manual `Win + L` remains effective.

## Local state

- Install directory: `%LOCALAPPDATA%\WindowsNeverSleep`
- Auto-start: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WindowsNeverSleep`
- Installed Apps entry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\WindowsNeverSleep`
- Rollback state: `%LOCALAPPDATA%\WindowsNeverSleep\state.ini`
- Runtime log: `%LOCALAPPDATA%\WindowsNeverSleep\windows-never-sleep.log`

The named mutex prevents duplicate runtime instances. A named event supports graceful `--stop` requests.
