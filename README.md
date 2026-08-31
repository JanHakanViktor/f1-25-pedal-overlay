# F1 25 Telemetry Overlay

A compact, transparent Windows overlay for **EA SPORTS F1 25**. It displays solid throttle and brake bars alongside a scrolling telemetry graph. An optional steering gauge can be shown to the graph's left or right.

![F1 25 Telemetry Overlay showing the optional steering gauge, telemetry graph, and pedal bars](docs/overlay-demo.png)

The production application is a native C#/.NET 10 WPF application. It uses a self-contained publish, so the installer does not require users to install .NET separately.

## Install on Windows (easy guide)

This is the only section most users need. No coding tools or terminal commands are required.

1. Open this project's **Releases** page on GitHub.
2. Download `F1-25-Telemetry-Overlay-Setup.exe` from the newest release.
3. Open the downloaded file. If Windows asks whether to run it, choose **Open**.
4. If SmartScreen says the publisher is unknown, select **More info**, confirm that the file came from this project's GitHub release, and choose **Run anyway**. Unsigned builds show this warning until the publisher signs the installer.
5. Choose **Install for all users** to use `C:\Program Files` (Windows will ask for administrator permission), or choose **Install for current user** to install without administrator permission.
6. On **Select Destination Location**, choose `C:\Program Files`, your Desktop, or another folder you can write to. The installer automatically creates an `F1 25 Telemetry Overlay` subfolder inside the location you choose, keeping the files together.
7. Leave **Create a desktop shortcut** selected if you want an icon on your desktop, then finish the installer.
8. Double-click **F1 25 Telemetry Overlay** from the desktop or Start menu. The application also places an icon beside the Windows clock.

The installer lets you choose **all users** (the default, which may install under `C:\Program Files`) or **current user** (which does not require administrator permission). It also shows a destination chooser so you can select another writable folder. Installing to `C:\Program Files` triggers the normal Windows administrator prompt. Leave **Create a desktop shortcut** enabled to put the icon on your Desktop; the application files themselves do not need to be stored there. The installer can be removed from **Settings → Apps → Installed apps** and leaves no .NET prerequisite behind.

## Configure telemetry in F1 25

Open **Settings → Telemetry Settings** in F1 25 and use:

- UDP Telemetry: **On**
- UDP Broadcast Mode: **Off** when the game and overlay are on the same PC
- UDP IP Address: **127.0.0.1**
- UDP Port: **20777**
- UDP Send Rate: **60 Hz**
- UDP Format: **2025**

Use borderless or windowed gameplay for reliable always-on-top behavior. True exclusive fullscreen can hide desktop overlays.

If Windows asks for network access, allow the application on **Private networks**. Only the local UDP telemetry stream is used; no telemetry is uploaded.

## Tray menu and settings

Right-click either the overlay itself or its icon beside the Windows clock to open the same menu:

- **Settings** opens the separate settings window.
- **Show/Hide overlay** changes only the overlay's visibility.
- **Lock/Unlock position** prevents or allows mouse interaction with the overlay.
- **Enable steering** shows or hides the steering gauge.
- **Exit** closes the application completely.

Settings let you choose whether steering starts enabled, its position, overlay transparency, UDP port, lock-up sensitivity, graph duration, every shortcut key, and the single colour used for every lock-up.

## Default controls

- Drag anywhere on the overlay to position it while it is unlocked.
- `Ctrl+Shift+O` locks or unlocks its position.
- `Ctrl+Shift+H` hides or shows it.
- `Ctrl+Shift+D` toggles the built-in demo signal.
- `Ctrl+Shift+S` toggles the steering gauge.
- `Ctrl+Shift+Q` exits the application.

All shortcut keys can be changed in **Settings**.

## Simple troubleshooting

- **The overlay is visible but the bars do not move:** Check every F1 25 telemetry setting above. Press `Ctrl+Shift+D` to confirm the overlay itself works with its demo signal.
- **The overlay disappears behind the game:** Change F1 25 from exclusive fullscreen to borderless mode.
- **The overlay is hidden and will not reopen:** Find its icon beside the Windows clock, right-click it, and select **Show overlay**. You may need to select the small **^** arrow first.
- **A shortcut does nothing:** Open **Settings** and choose a combination that is not already used by another application.
- **The installer is blocked:** Unsigned releases can trigger SmartScreen. Verify the download came from this repository before using **More info → Run anyway**. A signed release removes most of this warning.

## Port conflicts

Only one application can receive a loopback UDP port reliably on Windows. If port `20777` is already in use, choose another port in the overlay's **Settings** and enter that same port in F1 25.

## Build the native application from source

The following instructions are for developers or maintainers. Normal users should use the installer above.

### Build and test

1. Install the **.NET 10 SDK** for Windows x64 from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Download and extract this repository.
3. Open the extracted folder in File Explorer. Select the address bar, type `powershell`, and press **Enter**. A PowerShell window opens in the correct folder.
4. Run `dir`. You are in the right place when `F1TelemetryOverlay.slnx` and the `src` folder are listed.
5. Run these commands one at a time:

```powershell
dotnet restore .\src\F1TelemetryOverlay.Wpf\F1TelemetryOverlay.Wpf.csproj
dotnet test .\tests\F1TelemetryOverlay.Core.Tests\F1TelemetryOverlay.Core.Tests.csproj --configuration Release
powershell -ExecutionPolicy Bypass -File .\scripts\publish-native.ps1
```

The self-contained files are written to `artifacts\publish\win-x64`. The published executable is `F1-25-Telemetry-Overlay.exe`.

### Build the installer

The installer is made with Inno Setup 6 and includes the native app, its custom icon and the required runtime files. Install Inno Setup 6 once, for example:

```powershell
winget install --id JRSoftware.InnoSetup --exact --scope user
```

Then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-native-installer.ps1
```

The result is `artifacts\installer\F1-25-Telemetry-Overlay-Setup.exe`. It is a normal selectable per-user/all-users installer with a stable upgrade identity, a destination chooser, an optional desktop shortcut, a Start-menu shortcut and a normal uninstaller. The desktop and Start-menu shortcuts use `assets\app-icon.ico`.

### Build an unsigned local MSIX

The native app targets Windows 11 (minimum build 22000) and can be packed as an unsigned x64 MSIX using the Windows SDK `makeappx.exe` tool. A local development package must be explicit because it uses a development identity and is not a Store upload:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-native-msix.ps1 -Mode Local -AllowPlaceholderIdentity
```

The result is `artifacts\msix\x64\F1-25-Telemetry-Overlay.msix`. Store mode refuses to build until the exact Partner Center identity values are supplied; it never silently creates a placeholder Store package.

### Build release artifacts together

After installing Inno Setup 6 and the Windows 10 SDK, this command publishes the native app and creates the installer plus an explicit local MSIX:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-native-release.ps1 -AllowPlaceholderIdentity
```

For a real Store package, provide the exact Partner Center values through the `MSIX_*` environment variables and call `build-native-msix.ps1 -Mode Store`. See [Microsoft Store submission notes](docs/MICROSOFT-STORE.md).

Useful terminal navigation commands:

- `dir` lists files in the current folder.
- `cd "Folder Name"` enters a folder.
- `cd ..` goes back one folder.
- `cd /d "C:\full\path\to\folder"` jumps directly to a folder.

The native C# project is under `src\F1TelemetryOverlay.Core` and `src\F1TelemetryOverlay.Wpf`. The UDP parser and lock-up detector are isolated in the Core project, and the native tests are under `tests\F1TelemetryOverlay.Core.Tests`.
