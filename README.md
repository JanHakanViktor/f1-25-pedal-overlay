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
