# WinNote

A tiny Windows 11 scratchpad for quick notes and todo lists. It lives in the tray, pops up
instantly on a global hotkey, and hides again when you click away.

- Native WinUI 3 (Windows App SDK) app: Mica, Fluent controls, light/dark and accent colour.
- Plain-text notes with todo lines (`[ ] task` / `[x] done`) rendered with accent-coloured,
  clickable markers and strikethrough.
- Unpackaged, self-contained, Native AOT: copy the folder anywhere and run `WinNote.exe`.
- No network access, no telemetry, no accounts.

## Using it

| Action | Shortcut |
|---|---|
| Show / hide WinNote (global) | **Ctrl+Alt+N** (change in Settings) |
| Hide | **Esc**, the — button, Alt+F4, or click another window (unless pinned) |
| New note | **Ctrl+N** or + |
| Previous / next note | **Ctrl+← / Ctrl+→**, Ctrl+PgUp / Ctrl+PgDn, or the arrows |
| Delete note (undo from the toast for 5 s) | **Ctrl+W** or **Ctrl+Delete** |
| Undo / redo (per note) | Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) |
| Toggle todo, or turn a line into a todo | **Ctrl+Enter** (or click the `[ ]` marker) |
| Continue a todo list / end it | Enter / Enter on an empty `[ ] ` |
| Plain new line inside a list | Shift+Enter |
| Remove a todo marker | Backspace right after `[ ] ` |

- Typing `[] `, `- [ ] ` or `* [ ] ` at the start of a line converts it to `[ ] ` (Ctrl+Z reverts).
- The ⋯ menu has **Clear completed**, **Export as .txt…** and **Delete note**; the copy button copies
  the whole note.
- Pin (📌) keeps the window on top and visible when it loses focus.
- Tray icon: left-click toggles; right-click for Show, Pin, Settings, Start with Windows, Exit.
- Pasting always inserts plain text. Blank notes are removed when the window hides.

> Note: Ctrl+←/→ and Ctrl+Delete are used for notes (as specified), so the usual word-wise
> caret movement / delete-next-word aren't available in the editor. Ctrl+Shift+←/→ (select by
> word) and Ctrl+Backspace still work.

## Install

Double-click `WinNote-<version>-x64.msi` (built by `build-installer.ps1`, see below).

- Installs **for the current user only**, with no admin prompt, to `%LOCALAPPDATA%\Programs\WinNote`,
  adds a **Start menu** shortcut and an entry in **Settings → Apps → Installed apps**, then starts
  WinNote.
- **Upgrading**: run a newer MSI; a running WinNote is asked to save and close first. Your notes
  and the "Start with Windows" choice are kept.
- **Uninstalling** (Settings → Apps, or `msiexec /x WinNote-1.0.0-x64.msi`): closes WinNote,
  removes the program and its "Start with Windows" entry. **Notes and settings in
  `%LOCALAPPDATA%\WinNote` are kept**; delete that folder yourself to remove them.
- Silent install: `msiexec /i WinNote-1.0.0-x64.msi /qn` (doesn't auto-start WinNote).
- The MSI isn't code-signed, so Windows SmartScreen may warn when it's downloaded; choose
  "More info → Run anyway". Signing needs a code-signing certificate (`signtool sign …`).

## Requirements

**To run:** Windows 11 (build 22000 or later), x64 or ARM64. Nothing to install: the .NET runtime
and the Windows App SDK runtime are inside the published folder.

**To build:**

| What | Needed for | How to get it |
|---|---|---|
| .NET 10 SDK | everything | `winget install Microsoft.DotNet.SDK.10` |
| Visual Studio 2022/2026 (or Build Tools) with the **"Desktop development with C++"** workload (MSVC + Windows SDK) | Release publish (Native AOT links with MSVC) | Visual Studio Installer |
| Windows App SDK | — | comes from NuGet automatically; no separate install for unpackaged apps |

Visual Studio itself is optional: everything builds from the command line.

## Build and run (Debug)

```powershell
dotnet build src\WinNote\WinNote.csproj -c Debug -p:Platform=x64
.\src\WinNote\bin\x64\Debug\net10.0-windows10.0.22621.0\win-x64\WinNote.exe
```

## Tests

```powershell
dotnet test tests\WinNote.Tests\WinNote.Tests.csproj
```

54 xUnit tests for the UI-independent logic: todo parsing/toggling/auto-convert/clear-completed
(`TodoText`), undo grouping and limits (`EditHistory`), and crash-safe saving incl. the `.bak`
fallback (`AtomicFile`). The test project compiles those source files directly, so it needs no
WinUI or Windows App SDK.

## Build the installer

```powershell
powershell -ExecutionPolicy Bypass -File build-installer.ps1
```

This publishes the app (Native AOT) to `artifacts\app\win-x64\` and packs it into
`artifacts\WinNote-<version>-x64.msi` (~19 MB) using WiX Toolset 5 (from NuGet; nothing to
install). The version comes from `Directory.Build.props` (`WinNoteVersion`); bump it for every
release so the MSI upgrades the previous one.

WiX 5 is used on purpose: WiX 6+ requires accepting the Open Source Maintenance Fee EULA.

## Publish without an installer (Release, Native AOT)

```powershell
# Native AOT's linker setup calls vswhere.exe, which the VS Installer doesn't put on PATH:
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"

dotnet publish src\WinNote\WinNote.csproj -c Release -r win-x64 -p:Platform=x64 -o publish\win-x64
```

For ARM64 use `-r win-arm64 -p:Platform=ARM64 -o publish\win-arm64` (requires the MSVC ARM64
build tools component).

The result is a single self-contained folder, `publish\win-x64\` (~66 MB, 238 files): copy it
anywhere (or zip it) and run `WinNote.exe`. It is a folder rather than one .exe because WinUI 3
needs its native DLLs and resource index (`WinNote.pri`) next to the executable; single-file
publishing isn't supported for self-contained WinUI apps.

Size notes: the project references only the Windows App SDK components WinNote uses (WinUI,
Foundation, InteractiveExperiences, DWrite) instead of the `Microsoft.WindowsAppSDK` metapackage,
which would add ~45 MB of AI/ONNX/DirectML, Search and Widgets binaries. Debug symbols
(`WinNote.pdb`, 31 MB) are not copied to the publish folder.

## Data

Everything is stored in `%LOCALAPPDATA%\WinNote\`:

| File | Contents |
|---|---|
| `notes.json` | All notes. Text is plain, todos are literally `[ ] ` / `[x] `. |
| `settings.json` | Hotkey, window position/size, pin, theme, font, auto-delete, current note. |
| `*.bak` | The previous version of each file, used automatically if the main file is unreadable. |
| `error.log` | Only created if something unexpected happens (local only, capped at 256 KB). |

Writes are atomic: data goes to a temp file, is flushed to disk, then replaces the original
(keeping the old one as `.bak`), so a crash or power loss can't leave a half-written file.
Notes save 500 ms after you stop typing, when the window hides, on Exit, and at Windows
logoff/shutdown.

`settings.json` also has `"trimMemoryWhenHidden": true` (see below), which has no UI.

## Performance (measured on this build)

Release (Native AOT) build, x64, Windows 11 26100. Memory is Task Manager's "Memory" column
(private working set).

| Measurement | Result |
|---|---|
| Cold start (`--hidden`) until tray/hotkey ready | ~360 ms (measured from a script, approximate) |
| Show from hidden → window visible | **~5 ms** (first show after start: 8 ms) |
| Show from hidden → first frame rendered | **~5–7 ms** (first show after start: 30 ms) |
| Memory while visible | ~33–41 MB (higher after using Settings / the file dialog) |
| Memory while hidden (after automatic trim) | **~2–7 MB** |
| CPU while idle/hidden | 0 ms over 30 s |

To reproduce the show timing, start WinNote with the environment variable `WINNOTE_PERF=1`;
each show appends "show → first frame" to `%LOCALAPPDATA%\WinNote\perf.log`.

### What keeps it light

- **Native AOT + trimming**: no JIT, no IL, smaller and faster to start than a JIT build.
- **Window is created once and only hidden/shown**, never re-created; the settings page is built
  the first time it is opened.
- **Working-set trim on hide**: 3 s after hiding, WinNote collects the GC and asks Windows to trim
  its working set. Most of WinUI's footprint is start-up data touched once, so the Task Manager
  number drops from ~33 MB to ~2–7 MB, and the window still appears in ~5 ms. Private *commit*
  (~90–105 MB) is unchanged: trimmed pages move to Windows' standby list and are reused by other
  apps if RAM is needed. Set `trimMemoryWhenHidden` to `false` to turn this off.
- **Workstation, non-concurrent GC** (no background GC thread), invariant globalization.
- **No timers while idle**: autosave and toasts use one-shot timers that only run after input.
- **Native tray icon and menu** (Win32) instead of a XAML flyout window.

### Further ideas, if you want to go lower

(Estimates below are not measured.)

- Most of the ~31 MB measured right after a hidden start is WinUI/DirectComposition start-up;
  WinNote's own code and data are a small part, so the framework is effectively the floor
  without the trim.
- Replace the `RichEditBox` with a `TextBox`, dropping per-line todo styling: probably saves a
  few MB of the RichEdit/text-services stack.
- Destroy the window on hide and re-create it on show: lower idle commit, but show latency would
  rise to roughly 100–200 ms and caret/scroll state would need restoring.
- A Win32 + Direct2D (or WPF) rewrite could reach roughly 5–15 MB but loses the native Fluent
  controls.

## Packaging as MSIX (later)

The default build is unpackaged (`WindowsPackageType=None`). `src/WinNote/Package.appxmanifest`
and the logo PNGs are already in place. To package:

1. In `WinNote.csproj`, remove `<WindowsPackageType>None</WindowsPackageType>` and add
   `<EnableMsixTooling>true</EnableMsixTooling>`.
2. Replace `Services/StartupService.cs`'s Run-key logic with `Windows.ApplicationModel.StartupTask`
   (the manifest already declares the `WinNoteStartup` task); packaged apps can't use HKCU\Run.
3. Sign the package (a test certificate for side-loading, or the Store).

Data under `%LOCALAPPDATA%` keeps working; in a packaged app Windows redirects it into the
package's private folder.

## Project layout

```
Directory.Build.props     the version number (app + installer)
build-installer.ps1       publish + MSI in one step
installer/                WiX project (Package.wxs, WinNote.Installer.wixproj)
tests/WinNote.Tests/      xUnit tests for the pure logic
src/WinNote/
  Program.cs              entry point: single-instance mutex, forwards "show" to a running copy;
                          --hidden (autostart), --close / --uninstall (used by the installer)
  App.xaml(.cs)           tray menu, hotkey wiring, graceful exit / logoff save
  MainWindow.*.cs         window behaviour (show/hide/pin/auto-hide/placement), notes,
                          settings hosting, animation and memory trim
  Controls/NoteEditor*    plain-text RichEditBox: paste, per-note undo, todo rendering/keys/clicks
  Controls/TodoText.cs    pure todo text rules (parse, toggle, auto-convert, clear completed)
  Controls/EditHistory.cs snapshot-based undo/redo
  Controls/ActiveMicaBackdrop.cs  Mica that stays active while unfocused (pinned note)
  Views/SettingsView.*    settings page incl. shortcut recorder
  Services/               hotkey, tray icon, hidden host window, note/settings stores,
                          atomic file writes, startup (Run key), clipboard, error log
  Interop/NativeMethods   source-generated P/Invoke (RegisterHotKey, Shell_NotifyIcon, DWM, …)
tools/make-icon.ps1       regenerates the .ico and MSIX logo PNGs
```

Win32 interop is used only where WinUI has no API: the global hotkey, the tray icon and its
menu, foreground activation, DWM window attributes, and the dark-mode tray menu (uxtheme
`SetPreferredAppMode`, undocumented but used widely; it silently does nothing if unavailable).
