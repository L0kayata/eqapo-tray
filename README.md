# EqAPO Tray

A lightweight Windows tray GUI for controlling [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) gain without leaving the taskbar.

## Features

* Minimal UI that closely mirrors the native Windows volume flyout experience
* Click the tray icon to open a compact popup with a gain slider (-20 dB to +20 dB); changes apply instantly without confirmation
* Startup toggle checkbox in the top-left of the popup
* Config file picker button in the top-right, with a red/green status indicator showing whether the config file was loaded successfully
* Tray icon reflects the current gain: a speaker with 0 waves below -5 dB, 1 wave from -5 to +5 dB, 2 waves above +5 dB, and follows the light/dark taskbar theme

## Installation

Requirements: Windows 10 1809 or later (x64), with [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) installed and configured for your output device. No .NET or Windows App SDK runtime is needed; everything ships in the zip.

1. Download `EqApoTray-v<version>-win-x64.zip` from the [Releases](../../releases) page. Optionally check it against `SHA256SUMS.txt` from the same release:
   ```powershell
   (Get-FileHash .\EqApoTray-v1.0.0-win-x64.zip -Algorithm SHA256).Hash
   ```
2. Extract it to a permanent folder, e.g. `%LOCALAPPDATA%\Programs\EqApoTray`. Keep the folder intact: `EqApoTray.exe` needs the DLLs and resources next to it.
3. Run `EqApoTray.exe`. The exe is unsigned, so Windows SmartScreen may warn on first launch; choose "More info" → "Run anyway".

The app runs only in the tray. If you don't see its speaker icon, look in the taskbar overflow (`^`) and drag it onto the taskbar to keep it visible.

## Usage

The UI language follows your Windows display language: Simplified Chinese if Windows is set to Simplified Chinese, English otherwise (including Traditional Chinese). Labels below are given as English / 中文.

* **Left-click** the tray icon to open the popup; click anywhere outside it to close.
* Drag the slider (or click a point on the track) to set the Equalizer APO `Preamp` gain. The change is written to `config.txt` immediately and Equalizer APO picks it up on its own.
* **Config... / 配置...** (top-right) picks the Equalizer APO config file. The default is `C:\Program Files\EqualizerAPO\config\config.txt`; the dot next to the button is green when the file loaded and red when it didn't. If the file has no `Preamp:` line, the app adds one at the top.
* **Run at startup / 开机自启动** (top-left) starts the app when you sign in. It records the exe's current path, so if you move the folder later, untick and re-tick the box.
* **Right-click** the tray icon → **Exit / 退出** to quit.

Settings are stored in `%APPDATA%\EqApoTray\settings.json`.

To uninstall: untick Run at startup, quit the app, then delete its folder and `%APPDATA%\EqApoTray`.

## Credits

Tray icons, and the speaker in the app icon, are from [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) by Microsoft, MIT-licensed (see [icon/LICENSE](icon/LICENSE)).

The "EAT" lettering in the app icon is drawn from [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC) Bold, licensed under the SIL Open Font License 1.1 (see [icon/app/NotoSansSC-OFL.txt](icon/app/NotoSansSC-OFL.txt)).
