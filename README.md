# F1 25 Pedal Overlay

A compact, transparent Windows overlay for **EA SPORTS F1 25**. It displays live speed, solid throttle and brake bars, and a scrolling six-second input graph.

## Run it

1. Install [Node.js](https://nodejs.org/) 20 or newer.
2. In this folder, run:

   ```powershell
   npm install
   npm start
   ```

3. In F1 25, open **Settings → Telemetry Settings** and use:

   - UDP Telemetry: **On**
   - UDP Broadcast Mode: **Off** (when the overlay is on the same PC)
   - UDP IP Address: **127.0.0.1**
   - UDP Port: **20777**
   - UDP Send Rate: **60 Hz**
   - UDP Format: **2025**

Use borderless/windowed gameplay for reliable always-on-top behavior. True exclusive fullscreen can hide desktop overlays.

## Port conflicts

Only one application can receive a loopback UDP port reliably on Windows. If port `20777` is already in use, close the other telemetry application or launch this overlay on another port and enter the same port in F1 25:

```powershell
$env:F1_UDP_PORT = "20778"
npm start
```

## Controls

- Drag anywhere on the overlay to position it while it is unlocked.
- `Ctrl+Shift+O` toggles lock/edit mode.
- `Ctrl+Shift+H` hides or shows the overlay.
- `Ctrl+Shift+D` toggles the built-in demo signal.
- `Ctrl+Shift+Q` closes the overlay.

## Development

```powershell
npm test
```

The UDP parser is isolated in `src/telemetry/parser.ts`. It accepts the official F1 25 packet format (`2025`), packet ID `6`, and reads the player car selected by the packet header.
