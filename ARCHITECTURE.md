# EqApoTray — Architecture

A Windows-only system-tray utility that adjusts the global `Preamp` gain in
Equalizer APO's `config.txt` from a Win11-style flyout popup. Successor to the
Python/Tk prototype in [prototype/](prototype/).

## Stack

| Layer | Choice | Why |
|---|---|---|
| Language / runtime | C# 12, .NET 10 (`net10.0-windows10.0.19041.0`) | NativeAOT-capable; required TFM for Windows App SDK |
| UI framework | WinUI 3 (Windows App SDK 1.8.x) | First-party Win11 Fluent UI, Native-AOT compatible, system Acrylic/Mica via `SystemBackdrop` |
| Tray icon | [`H.NotifyIcon.WinUI`](https://github.com/HavenDV/H.NotifyIcon) | Same author as the WPF variant; WinUI 3 build of the tray library |
| Theming | Built-in WinUI 3 Fluent | No third-party theme; matches Win11 system UI exactly |
| Build | `dotnet` CLI + VS Code | Lighter dev loop; no Visual Studio required for Debug |
| Packaging | `dotnet publish` → unpackaged self-contained (`WindowsPackageType=None`, optional `PublishAot=true`) | No MSIX, no runtime install on user machine |

### NativeAOT prerequisite

`PublishAot=true` (Release builds, see `scripts/publish.ps1`) requires the
**Visual Studio Desktop Development with C++** workload (specifically
`link.exe` and the Windows SDK linker). Without it `dotnet publish` fails with
"Platform linker not found" — see https://aka.ms/nativeaot-prerequisites. The
non-AOT build (`-p:PublishAot=false`) does not need this.

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
│   ├── App.xaml(+.cs)               # entry point; owns TaskbarIcon + FlyoutWindow
│   ├── FlyoutWindow.xaml(+.cs)      # borderless Acrylic host window for the flyout
│   ├── FlyoutControl.xaml(+.cs)     # the popup UserControl content
│   └── Services/
│       ├── EqApoConfig.cs           # read/write `Preamp: X.X dB` line
│       ├── StartupService.cs        # HKCU\…\Run registry toggle
│       ├── SettingsStore.cs         # JSON in %APPDATA%\EqApoTray\settings.json
│       │                              # AOT-safe via JsonSerializerContext
│       ├── TrayPopupPositioner.cs   # precise Shell_NotifyIconGetRect anchor
│       └── Win32FileDialog.cs       # GetOpenFileNameW wrapper (FileOpenPicker
│                                      # is unreliable in unpackaged WinUI 3)
└── prototype/                       # original Python/Tk prototype (frozen)
    ├── eqapo_tray.py
    ├── eqapo-tray.spec              # PyInstaller spec
    └── .gitignore                   # Python-only ignores
```

## Module responsibilities

- **`App.xaml.cs`** — `OnLaunched` constructs the `TaskbarIcon` programmatically
  with `ContextMenuMode = ContextMenuMode.PopupMenu` (Win32 native menu, see
  "WinUI 3 quirks" below) and a single "退出" `MenuFlyoutItem` whose
  `Command` (not `Click`) is wired to `QuitApplication`. `LeftClickCommand`
  toggles `FlyoutWindow`. There is no main window and no `ShutdownMode`
  equivalent — quit calls `Environment.Exit(0)` after disposing the tray
  icon.
- **`FlyoutWindow`** — Borderless WinUI 3 `Window` that hosts a single
  `FlyoutControl`. Uses `OverlappedPresenter.Create()` with
  `SetBorderAndTitleBar(false, false)` to strip chrome. Topmost is set
  manually via `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` after each show,
  *not* via `OverlappedPresenter.IsAlwaysOnTop` (which triggers a focus-pull
  bug — see "WinUI 3 quirks"). Click-outside dismiss is implemented with a
  `WH_MOUSE_LL` low-level mouse hook installed in `ShowAt` and uninstalled in
  `HideFlyout`; the hook callback queues `HideFlyout` via the dispatcher when
  the cursor at click-time is outside the window's `GetWindowRect`.
  `DesktopAcrylicBackdrop` provides the Acrylic. Positioning uses
  `AppWindow.MoveAndResize` in physical pixels, computed from
  `GetDpiForWindow`. A 220 ms `Storyboard` (slide-up + fade) replays on each
  show.
- **`FlyoutControl`** — Stateless w.r.t. its host except for the injected
  `DialogOwnerHwnd`. On `Loaded` and on each show (via `RefreshFromDisk`,
  called from `FlyoutWindow.ShowAt`) it pulls the current `Preamp` value,
  autostart bit, and config-path status from disk. The slider's
  `ValueChanged` is debounced through a 60 ms `DispatcherQueueTimer` to
  avoid hammering `config.txt`. Track click-to-jump is implemented via
  `PointerPressed/Moved/Released/CaptureLost` on the `Slider` itself —
  WinUI 3's `Slider` has no public `PART_Track` template hook like WPF, so
  pointer position is converted directly to a value via
  `slider.ActualWidth`. Errors raise a `ContentDialog` rooted on the
  control's `XamlRoot`. The "配置..." button calls `Win32FileDialog.PickFile`.
- **`Services/EqApoConfig`** — Single regex (`Preamp:\s*([-\d.]+)\s*dB`)
  matches both reading and writing. Format is locked to `InvariantCulture`
  (`F1`) so a German locale doesn't write `Preamp: 3,5 dB`.
- **`Services/StartupService`** — Writes the current process path to
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. HKCU only — no admin.
- **`Services/SettingsStore`** — Persists the user-chosen config-file path to
  `%APPDATA%\EqApoTray\settings.json` via the `SettingsJsonContext` source
  generator (AOT-safe; reflection-based serialization is incompatible with
  trimmed/AOT builds).
- **`Services/TrayPopupPositioner`** — Uses `Shell_NotifyIconGetRect` to query
  the actual notification icon rectangle, including when the icon is clicked
  from the overflow tray. Converts physical pixels to DIPs and clamps the
  flyout to the current monitor work area. The WinUI host re-converts back
  to physical pixels for `AppWindow.MoveAndResize`.
- **`Services/Win32FileDialog`** — `GetOpenFileNameW` wrapper. Replaces
  `Windows.Storage.Pickers.FileOpenPicker`, which throws `COMException`
  in unpackaged WinUI 3 apps under various activation-context conditions.
  The classic dialog has no such requirements and works the same whether
  the app is packaged or not. Filter strings use the Win32 `\0`-separated
  format. AOT-friendly: blittable struct + `[LibraryImport]`.

## Key flows

**Tray click → flyout shown.** `App.ToggleFlyout` (wired via
`TaskbarIcon.LeftClickCommand`) asks `TrayPopupPositioner` for the real icon
rectangle, computes the popup placement, and calls
`FlyoutWindow.ShowAt(anchor)`. The window is moved + resized in physical
pixels via `AppWindow.MoveAndResize`, shown with `activateWindow:true`,
manually pinned topmost via `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)`,
foregrounded via `SetForegroundWindow` (works around #7595), and the
`WH_MOUSE_LL` mouse hook is installed. The flyout root animates in with a
220 ms slide-up + fade via `Storyboard`, replayed on every reopen.

**Click outside → flyout dismissed.** The mouse hook runs on the UI thread
for every `WM_LBUTTONDOWN/RBUTTONDOWN/MBUTTONDOWN` system-wide. If the
cursor is outside the window's `GetWindowRect`, the callback enqueues
`HideFlyout` on the dispatcher (does *not* run it synchronously — see "Tray
click toggle" below) and returns; the click still propagates to whatever
the user actually clicked on. `HideFlyout` then `AppWindow.Hide()`s the
window and uninstalls the hook. This bypasses WinUI 3's `Window.Activated`
event, which is unreliable after Hide/Show cycles.

**Tray click → flyout toggled.** A second click on the tray icon retracts the
flyout. The mouse hook fires first (the click is outside the flyout's rect),
enqueueing `HideFlyout`. The OS message then propagates to the tray icon and
H.NotifyIcon raises `LeftClickCommand` — but `LeftClickCommand` runs
synchronously *before* the dispatcher gets to drain its queue, so
`ToggleFlyout` reads `IsOpen == true` and synchronously calls `HideFlyout`
itself. The queued copy from the hook runs second, sees `IsOpen == false`,
and no-ops via the guard. Net effect: a single, reliable close. The
deferred-via-dispatcher pattern is what makes this race-free; an Activated
event handler that ran `HideFlyout` synchronously would set `IsOpen = false`
before `ToggleFlyout` could read it, and the second click would silently
reopen.

**Config picker → Win32 GetOpenFileNameW.** The "配置..." button calls
`Win32FileDialog.PickFile(DialogOwnerHwnd, …)`, which marshals the dialog
title and `\0`-separated filter into native heap, invokes
`comdlg32.GetOpenFileNameW`, and reads the returned path back. The dialog's
own message pump runs while it's modal; the flyout deactivates and the
mouse hook inevitably hides it (clicking inside the file dialog is "outside"
our flyout). After picking, the new path is saved and the user re-clicks
the tray to see the refreshed UI — same UX shape as the WPF prototype.

**Slider drag → file write.** Each `ValueChanged` resets a 60 ms timer; on
tick we write the new `Preamp` line atomically (full read-modify-write of
`config.txt`). The status dot colour reflects the last write outcome. If the
file is missing or unwritable (typical when EqAPO is in `Program Files` and
the app runs without elevation), the dot turns red and we surface the error
visually rather than silently swallowing it.

**Startup checkbox → registry.** Direct, synchronous. Failure raises a
`ContentDialog` (WinUI 3) rooted on the control's `XamlRoot`, and the
checkbox is reverted to the actual registry state.

## WinUI 3 quirks worked around

These are *not* general WinUI 3 patterns to copy elsewhere — they are
defensive workarounds for behaviour that is broken or unsupported in the
specific configuration this app uses (unpackaged, single reused `Window`,
no main window). Removing them will reproduce the original bug.

- **`OverlappedPresenter.IsAlwaysOnTop = true` triggers a focus-pull bug**
  ([microsoft-ui-xaml#9990](https://github.com/microsoft/microsoft-ui-xaml/issues/9990)).
  After the first `AppWindow.Hide()`+`Show()` cycle the window's activation
  state machine sticks: clicking outside briefly transfers focus (visible as
  a one-frame desktop-icon flicker) but is immediately yanked back, so
  `WM_ACTIVATE WA_INACTIVE` is never delivered — even a raw
  `comctl32.SetWindowSubclass` doesn't see it. We use raw
  `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` instead, which is bug-free.
- **`Window.Activated` / `WM_ACTIVATE` are unreliable for click-outside
  dismiss** in this scenario, by extension of the same bug. The mouse hook
  is the only mechanism that works on every cycle. It's a global
  `WH_MOUSE_LL` hook but installed only while the flyout is visible, so
  the system-wide cost is negligible.
- **Programmatic `TaskbarIcon` has no `XamlRoot`**, so the default
  `ContextMenuMode = SecondWindow` silently drops `MenuFlyoutItem.Click`
  events. We use `ContextMenuMode = ContextMenuMode.PopupMenu` (native
  Win32 menu) which dispatches via `Command` instead — see
  [`TaskbarIcon.ContextMenu.WinRT.PopupMenu.cs PopulateMenu`](https://github.com/HavenDV/H.NotifyIcon/blob/master/src/libs/H.NotifyIcon.Shared/TaskbarIcon.ContextMenu.WinRT.PopupMenu.cs).
- **`Windows.Storage.Pickers.FileOpenPicker` throws `COMException` in
  unpackaged apps** under various activation-context conditions. We use a
  `comdlg32.GetOpenFileNameW` wrapper (`Services/Win32FileDialog`) which
  has no such requirement.
- **`Application.Current.Exit()` doesn't reliably terminate** a tray-only
  app (no main window for the WinUI lifetime to hang off). The Quit menu
  uses `_trayIcon.Dispose()` + `Environment.Exit(0)`.
- **`dotnet publish` drops the XAML/MRT artifacts** for unpackaged WinUI 3.
  The XAML compiler writes `*.xbf` to `obj\<Config>\<TFM>\<RID>\` and the
  resource index `<AssemblyName>.pri` to `bin\<Config>\<TFM>\<RID>\`, but
  neither is added to `ResolvedFileToPublish`, so they never reach the
  publish output. Without them `Microsoft.UI.Xaml.dll` throws a stowed
  exception (Windows Error Reporting hash `0xc000027b`, faulting module
  `Microsoft.UI.Xaml.dll`) at process start because `InitializeComponent()`
  can't resolve `App.xaml`. The csproj has a `_IncludeWinUIArtifactsInPublish`
  target that injects both file sets before `ComputeFilesToPublish`. Affects
  both AOT and non-AOT publish; running the bin output directly is
  unaffected because the build already lays the files next to the exe.

## Conventions / invariants

- **Globalization:** every numeric parse/format on `Preamp` uses
  `CultureInfo.InvariantCulture`. Don't introduce `ToString("F1")` without a
  culture argument.
- **Threading:** all UI work stays on the dispatcher thread. There's no
  background work that needs `Task.Run` yet; if file I/O ever moves async,
  watch for re-entrancy on the slider event.
- **Tray icon source:** `H.NotifyIcon.WinUI` exposes `IconSource` (XAML
  `IconSource`) which the library converts to a `System.Drawing.Icon`
  internally. The icon is intentionally left unset right now — see "Known
  constraints / future work" — so the tray entry registers without an image
  while a custom icon design is pending. The project enables
  `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` only because the
  `[LibraryImport]` source generator emits unsafe marshalling code for the
  P/Invokes in `TrayPopupPositioner`, `FlyoutWindow`, and `Win32FileDialog`.
- **Dialog ownership:** `Win32FileDialog` and `ContentDialog` both need an
  owner. `FlyoutControl.DialogOwnerHwnd` is set by `FlyoutWindow` at
  construction time and used as the file dialog's `hwndOwner`; the
  `XamlRoot` for `ContentDialog` is the `UserControl`'s own `XamlRoot`. Do
  not call `FileOpenPicker.PickSingleFileAsync` — see "WinUI 3 quirks".
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

# Dev loop
dotnet build src/EqApoTray/EqApoTray.csproj
# or VS Code task "build" (Ctrl+Shift+B), or F5 for debugger attach

# Release Native AOT (requires VS C++ Desktop workload, see Stack > NativeAOT)
pwsh scripts/publish.ps1
# → dist/EqApoTray-win-x64/                  staging directory (unzipped)
#   dist/EqApoTray-v<Version>-win-x64.zip    GitHub release artifact
#   dist/SHA256SUMS.txt                      GitHub release artifact
```

`publish.ps1` runs four steps: (1) `dotnet publish` AOT/self-contained
into the staging directory, (2) strip components this app does not load
(AI/ML stack, Widgets, WebView2, `*.pdb`, and locale `*.mui` folders
other than `zh-CN`/`en-us` — see "Known constraints" below; net result
~60 MB unzipped, ~30 MB zipped), (3) zip the staging directory,
(4) write `SHA256SUMS.txt`. The zip filename's version comes from
`<Version>` in `EqApoTray.csproj`, which is the single source of truth
— bump it once and tag the git commit with the matching `v<Version>`.

VS Code tasks (`.vscode/tasks.json`): `build` (default, Ctrl+Shift+B),
`watch`, `publish`. Recommended extensions in `.vscode/extensions.json`:
C# Dev Kit + C#.

## Known constraints / future work

- **EqAPO file lock.** Equalizer APO's audio service watches `config.txt` and
  reloads on each write. Rapid writes are fine in practice (debounced to
  60 ms) but tearing is possible on slow disks — if it shows up, switch to
  write-temp-then-rename.
- **Tray icon image.** `App.OnLaunched` does not set
  `TaskbarIcon.IconSource`; the library still registers the tray entry but
  it has no image. Custom icon work is deferred — drop in either an `.ico`
  asset (`<Content Include="Assets/tray.ico" />`) and assign
  `IconSource = new BitmapImage(new Uri(...))`, or use H.NotifyIcon's
  `GeneratedIcon` for a code-rendered icon.
- **WindowsAppSDK self-contained size.** `WindowsAppSDKSelfContained=true`
  drags in transitive AI/ML runtimes (`onnxruntime.dll`, `DirectML.dll`,
  `Microsoft.Windows.AI.*`, Imaging, Workloads, `NpuDetect/`),
  `Microsoft.Windows.Widgets`, WebView2, and `WinUIEdit`, totalling
  ~150 MB raw. WASDK 1.6 had a much smaller footprint but is
  incompatible with .NET 10 (`Microsoft.Build.Packaging.Pri.Tasks` load
  failure). `publish.ps1` works around it with a post-publish strip
  (~150 MB raw → ~60 MB unzipped, ~30 MB zipped): see
  `$stripFilePatterns` / `$stripDirs` / `$keepLocales` in the script.
  Anything that touches XAML, composition, MRT, or the
  WindowsAppRuntime bootstrap chain is **not** stripped — if a future
  feature needs Widgets / WebView2 / AI / a different locale, drop the
  matching pattern from the strip list rather than disabling the strip
  wholesale. **Landmine:** `WinUIEdit.dll` looks like dead weight
  (this app has no `RichEditBox`) but WinUI 3 crashes at startup
  without it on WASDK 1.8. It is *not* in the strip list and the
  publish script carries an explicit comment to keep curious
  maintainers from "tidying" it up. The alternative path
  (`WindowsAppSDKSelfContained=false`, user installs Windows App
  Runtime separately) is rejected because the ~30 MB runtime
  installer is a worse UX than a fatter zip.
- **NativeAOT prerequisite.** `PublishAot=true` needs `link.exe` from the
  Visual Studio C++ Desktop workload. CI/local builds without it fall back
  to JIT publish via `-p:PublishAot=false`.
- **Theming.** WinUI 3 follows the system theme automatically through the
  default `XamlControlsResources` — no manual theme switch needed.

## What lives in `prototype/`

The original Python/Tk implementation, kept as a reference for behaviour
(particularly the `Preamp` regex semantics and registry key name). Not
maintained. Build with `pyinstaller eqapo-tray.spec` from inside that
directory if you ever need to compare behaviour. Its `.gitignore` is scoped
to that subtree only.
