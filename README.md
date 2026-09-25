# Windows Keyboard Lock

A small Windows tray utility that globally locks keyboard input with a toggle shortcut.

## Usage

Download and run [KeyboardLock.exe](outputs/KeyboardLock.exe). Press **Ctrl+Alt+K** to lock or unlock keyboard input. The tray icon also provides lock, unlock, and exit actions.

The program installs a low-level keyboard hook and does not modify keyboard drivers. `Ctrl+Alt+Delete`, Windows sign-in, and UAC secure desktop screens cannot be intercepted by ordinary desktop applications.

## Source and build

- [KeyboardLock.cs](work/KeyboardLock.cs) — standalone WinForms source.
- [KeyboardLock.exe](outputs/KeyboardLock.exe) — compiled Windows executable.
- [KeyboardLock.ps1](outputs/KeyboardLock.ps1) — earlier PowerShell implementation.
- [Start-KeyboardLock.cmd](outputs/Start-KeyboardLock.cmd) — PowerShell launcher for the earlier implementation.

The executable can be compiled with the .NET Framework C# compiler:

```powershell
csc /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll KeyboardLock.cs
```
