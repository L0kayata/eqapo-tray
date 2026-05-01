# EqApoTray — Architecture

A Windows-only system-tray utility that adjusts the global `Preamp` gain in
Equalizer APO's `config.txt` from a Win11-style flyout popup. Successor to the
Python/Tk prototype in [prototype/](prototype/).

## Stack

| Layer | Choice | Why |
|---|---|---|
| Language / runtime | C# 12, .NET 10 (`net10.0-windows`) | LTS, NativeAOT-capable, latest WPF |
| UI framework | WPF | Stable; mature tray-icon ecosystem (WinUI 3 has no first-class tray support) |
| Tray icon | [`H.NotifyIcon.Wpf`](https://github.com/HavenDV/H.NotifyIcon) | Active fork of `Hardcodet.NotifyIcon.Wpf`; supports `TrayPopup` flyouts |
| Theming | [`WPF-UI`](https://github.com/lepoco/wpfui) | Win11 Fluent look (Mica-ish), modern control restyles |
| Build | `dotnet` CLI + VS Code (no Visual Studio required) | Lighter dev loop |
| Packaging | `dotnet publish` → single self-contained EXE (`PublishSingleFile`, optional NativeAOT later) | No runtime install on user machine |

## Repository layout

```
eqapo-tray/
├── EqApoTray.sln                    # solution (one project for now)
├── ARCHITECTURE.md                  # ← this file
├── README.md                        # user-facing description
├── .gitignore                       # .NET-only ignores
├── .vscode/                         # VS Code: build/run/debug tasks + extension recs
│   ├── launch.json
│   ├── tasks.json
│   └── extensions.json
├── scripts/
│   └── publish.ps1                  # one-shot single-file Release build
├── src/EqApoTray/                   # the only application project
│   ├── EqApoTray.csproj
│   ├── app.manifest                 # PerMonitorV2 DPI, asInvoker, Win10/11 target
│   ├── App.xaml(+.cs)               # entry point; owns the TaskbarIcon
│   ├── FlyoutControl.xaml(+.cs)     # the popup UserControl
│   └── Services/
│       ├── EqApoConfig.cs           # read/write `Preamp: X.X dB` line
│       ├── StartupService.cs        # HKCU\…\Run registry toggle
│       ├── SettingsStore.cs         # JSON in %APPDATA%\EqApoTray\settings.json
│       └── TrayIconFactory.cs       # programmatic 32×32 tray icon
└── prototype/                       # original Python/Tk prototype (frozen)
    ├── eqapo_tray.py
    ├── eqapo-tray.spec              # PyInstaller spec
    └── .gitignore                   # Python-only ignores
```

## Module responsibilities

- **`App.xaml.cs`** — `OnStartup` constructs the `TaskbarIcon` programmatically,
  attaches a single shared `FlyoutControl` as `TrayPopup`, and builds a
  right-click `ContextMenu` with "Open / Quit". `ShutdownMode` is
  `OnExplicitShutdown`: closing the flyout never exits the app; only the Quit
  menu item or `Application.Shutdown()` does.
- **`FlyoutControl`** — Stateless w.r.t. its host. On `Loaded` it pulls the
  current `Preamp` value, autostart bit, and config-path status from disk and
  populates the UI. The slider's `ValueChanged` is debounced through a 60 ms
  `DispatcherTimer` to avoid hammering `config.txt` (which Equalizer APO
  watches and reloads on each change). Status dot turns red on any I/O failure.
- **`Services/EqApoConfig`** — Single regex (`Preamp:\s*([-\d.]+)\s*dB`)
  matches both reading and writing. Format is locked to `InvariantCulture`
  (`F1`) so a German locale doesn't write `Preamp: 3,5 dB`.
- **`Services/StartupService`** — Writes the current process path to
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. HKCU only — no admin.
- **`Services/SettingsStore`** — Persists the user-chosen config-file path to
  `%APPDATA%\EqApoTray\settings.json`. The prototype put it next to the EXE,
  which fails when the EXE lives in `Program Files`.
- **`Services/TrayIconFactory`** — Renders a 32×32 blue circle with "dB" text
  via `DrawingVisual` → `RenderTargetBitmap`. Avoids shipping an `.ico` asset.

## Key flows

**Tray click → flyout shown.** `H.NotifyIcon` shows `TrayPopup` as a Win32
popup window anchored to the tray icon, auto-closes on outside click. No
manual positioning code needed — that's the whole reason this library was
chosen over rolling our own `NotifyIcon`.

**Slider drag → file write.** Each `ValueChanged` resets a 60 ms timer; on
tick we write the new `Preamp` line atomically (full read-modify-write of
`config.txt`). The status dot colour reflects the last write outcome. If the
file is missing or unwritable (typical when EqAPO is in `Program Files` and
the app runs without elevation), the dot turns red and we surface the error
visually rather than silently swallowing it — that's an upgrade over the
Python version.

**Startup checkbox → registry.** Direct, synchronous; failure raises a
`MessageBox` and reverts the checkbox.

## Conventions / invariants

- **Globalization:** every numeric parse/format on `Preamp` uses
  `CultureInfo.InvariantCulture`. Don't introduce `ToString("F1")` without a
  culture argument.
- **Threading:** all UI work stays on the dispatcher thread. There's no
  background work that needs `Task.Run` yet; if file I/O ever moves async,
  watch for re-entrancy on the slider event.
- **No MVVM framework.** The app is small enough that code-behind is
  honest and shorter than wiring up `INotifyPropertyChanged` plumbing.
  Don't add CommunityToolkit.Mvvm for the sake of it.
- **No admin manifest.** `level="asInvoker"`. Writing to
  `C:\Program Files\EqualizerAPO\config\config.txt` requires elevation; we
  surface the failure rather than auto-elevating, because elevating a
  startup-on-boot tray app is hostile UX.
- **Settings location:** `%APPDATA%\EqApoTray\` only. Never write next to the
  EXE.

## Build / run / publish

```powershell
# First time
dotnet restore EqApoTray.sln

# Dev loop (hot reload via dotnet watch)
dotnet watch --project src/EqApoTray run
# or VS Code task "watch", or F5 for debugger attach

# Release single-file
pwsh scripts/publish.ps1
# → dist/EqApoTray-win-x64/EqApoTray.exe (~30–40 MB self-contained)
```

VS Code tasks (`.vscode/tasks.json`): `build` (default, Ctrl+Shift+B),
`watch`, `publish`. Recommended extensions in `.vscode/extensions.json`:
C# Dev Kit + C#.

## Known constraints / future work

- **EqAPO file lock.** Equalizer APO's audio service watches `config.txt` and
  reloads on each write. Rapid writes are fine in practice (debounced to
  60 ms) but tearing is possible on slow disks — if it shows up, switch to
  write-temp-then-rename.
- **Flyout positioning.** Currently relies on `H.NotifyIcon`'s default popup
  placement, which is "near the tray icon". If we want exact parity with the
  Win11 volume flyout (slide-up animation, anchored above taskbar), we'd
  hook `Shell_NotifyIconGetRect` directly via P/Invoke.
- **NativeAOT.** WPF + AOT is supported in .NET 8+ but with caveats (XAML
  reflection, some package incompatibilities). Worth revisiting once
  `WPF-UI` and `H.NotifyIcon.Wpf` declare AOT compatibility — would shrink
  the EXE from ~35 MB to ~15 MB.
- **Theming.** Currently hard-coded to Light via `ThemesDictionary Theme="Light"`,
  with `ApplicationThemeManager.ApplySystemTheme()` called at startup. If the
  user's system theme changes at runtime, we don't react — fix by subscribing
  to `SystemEvents.UserPreferenceChanged`.

## What lives in `prototype/`

The original Python/Tk implementation, kept as a reference for behaviour
(particularly the `Preamp` regex semantics and registry key name). Not
maintained. Build with `pyinstaller eqapo-tray.spec` from inside that
directory if you ever need to compare behaviour. Its `.gitignore` is scoped
to that subtree only.
