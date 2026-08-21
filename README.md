# F1 25 Pedal Overlay

A compact, transparent Windows overlay for **EA SPORTS F1 25**. It displays solid throttle and brake bars alongside a scrolling telemetry graph.

An optional steering gauge can be shown to the graph's left. The game reports steering as a normalized value, so the gauge maps full lock to `-180°` and `180°`.

![F1 25 Pedal Overlay showing the optional steering gauge, telemetry graph, and pedal bars](docs/overlay-demo.png)

## Install on Windows

No coding tools or terminal are required.

1. Open this repository's **Releases** page and download `F1-25-Pedal-Overlay-Setup.exe` from the newest release.
2. Open the downloaded file and let the installer finish. The overlay starts automatically and a shortcut is added for later.
3. If Windows SmartScreen appears because the app is not code-signed, select **More info** and then **Run anyway** only when the installer came from this repository.
4. Start **F1 25 Pedal Overlay** from its shortcut whenever you want to use it.

The application stays available in the Windows system tray. Hiding the overlay does not exit the application; use **Exit** in the tray menu or press `Ctrl+Shift+Q`.

## Configure telemetry in F1 25

Open **Settings → Telemetry Settings** in F1 25 and use:

- UDP Telemetry: **On**
- UDP Broadcast Mode: **Off** when the game and overlay are on the same PC
- UDP IP Address: **127.0.0.1**
- UDP Port: **20777**
- UDP Send Rate: **60 Hz**
- UDP Format: **2025**

Use borderless or windowed gameplay for reliable always-on-top behavior. True exclusive fullscreen can hide desktop overlays.

If Windows asks for network access, allow the app on **Private networks**. Borderless or windowed gameplay is recommended; true exclusive fullscreen can hide normal desktop overlays.

## Tray menu and settings

Right-click either the overlay itself or its icon in the Windows system tray to open the same menu:

- **Settings** opens the separate settings window.
- **Show/Hide overlay** changes only the overlay's visibility.
- **Lock/Unlock position** prevents or allows mouse interaction with the overlay.
- **Enable steering** shows or hides the steering gauge.
- **Exit** closes the application completely.

Settings let you choose whether steering starts enabled, overlay transparency, UDP port, lock-up sensitivity, graph duration, every shortcut key, and lock-up colours. Lock-up colours can be separate for the front, rear and both axles, or one yellow colour can be used for every lock-up.

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

## Port conflicts

Only one application can receive a loopback UDP port reliably on Windows. If port `20777` is already in use, choose another port in the overlay's **Settings** and enter that same port in F1 25.

## Build from source

This section is only for developers. Normal users should use the installer above.

1. Install the current [Node.js LTS](https://nodejs.org/) version.
2. Download and extract this repository.
3. Open the extracted folder in File Explorer. Select the address bar, type `cmd`, and press **Enter**. A terminal opens in the correct folder.
4. Run `dir`. You are in the right place when `package.json` is listed.
5. Run these commands one at a time:

```bat
npm install
npm test
npm run make
```

The finished installer is written to `out\make\squirrel.windows\x64\F1-25-Pedal-Overlay-Setup.exe`.

Useful terminal navigation commands:

- `dir` lists the files in the current folder.
- `cd "Folder Name"` enters a folder.
- `cd ..` goes back one folder.
- `cd /d "C:\full\path\to\folder"` jumps directly to a folder.

For local development without installing:

```powershell
npm start
```

Run the automated checks with:

```powershell
npm test
```

The UDP parser is isolated in `src/telemetry/parser.ts`. It accepts the official F1 25 packet format (`2025`), packet ID `6`, and reads the player car selected by the packet header.
