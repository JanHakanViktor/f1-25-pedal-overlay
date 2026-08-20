import { app, BrowserWindow, globalShortcut, ipcMain, screen } from "electron";
import path from "node:path";
import { TelemetryServer } from "./telemetry/server";
import type { OverlaySnapshot, OverlayStatus, PedalTelemetry } from "./shared";

const portArgument = app.commandLine.getSwitchValue("udp-port")
  || process.argv.find((argument) => argument.startsWith("--udp-port="))?.split("=")[1];
const requestedPort = Number.parseInt(portArgument ?? process.env.F1_UDP_PORT ?? "20777", 10);
const UDP_PORT = Number.isInteger(requestedPort) && requestedPort > 0 && requestedPort <= 65535
  ? requestedPort
  : 20777;

const OVERLAY_WIDTH = 460;
const STEERING_GAUGE_WIDTH = 141;
const OVERLAY_HEIGHT = 150;

let window: BrowserWindow | null = null;
let locked = false;
let demoEnabled = false;
let steeringEnabled = app.commandLine.hasSwitch("steering")
  || process.argv.includes("--steering")
  || process.env.F1_OVERLAY_STEERING === "1";
let demoTimer: NodeJS.Timeout | null = null;
let lastTelemetry: PedalTelemetry = {
  speedKph: 0,
  throttle: 0,
  steering: 0,
  brake: 0,
  timestamp: 0
};
let lastStatus: OverlayStatus = {
  state: "listening",
  message: `Waiting on UDP ${UDP_PORT}`,
  port: UDP_PORT
};
const telemetryServer = new TelemetryServer(UDP_PORT);

function createWindow(): void {
  const workArea = screen.getPrimaryDisplay().workArea;
  const windowWidth = OVERLAY_WIDTH + (steeringEnabled ? STEERING_GAUGE_WIDTH : 0);

  window = new BrowserWindow({
    width: windowWidth,
    height: OVERLAY_HEIGHT,
    x: workArea.x + workArea.width - windowWidth - 40,
    y: workArea.y + Math.round((workArea.height - OVERLAY_HEIGHT) / 2),
    transparent: true,
    frame: false,
    resizable: false,
    alwaysOnTop: true,
    hasShadow: false,
    backgroundColor: "#00000000",
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true
    }
  });

  window.setAlwaysOnTop(true, "screen-saver");
  window.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });
  window.loadFile(path.join(__dirname, "renderer", "index.html"));
  window.webContents.on("did-finish-load", () => {
    window?.webContents.send("lock-changed", locked);
    window?.webContents.send("demo-changed", demoEnabled);
    window?.webContents.send("steering-changed", steeringEnabled);
    window?.webContents.send("status", demoEnabled
      ? { state: "connected", message: "Demo signal", port: UDP_PORT }
      : lastStatus
    );
  });
  window.on("closed", () => {
    window = null;
  });
}

function setSteeringEnabled(enabled: boolean): void {
  if (steeringEnabled === enabled) return;
  steeringEnabled = enabled;

  if (window) {
    const bounds = window.getBounds();
    const nextWidth = OVERLAY_WIDTH + (steeringEnabled ? STEERING_GAUGE_WIDTH : 0);
    const widthDelta = nextWidth - bounds.width;
    window.setResizable(true);
    window.setBounds({
      x: bounds.x - widthDelta,
      y: bounds.y,
      width: nextWidth,
      height: OVERLAY_HEIGHT
    });
    window.setResizable(false);
    window.webContents.send("steering-changed", steeringEnabled);
  }
}

function setLocked(nextLocked: boolean): void {
  locked = nextLocked;
  window?.setIgnoreMouseEvents(locked, { forward: true });
  window?.webContents.send("lock-changed", locked);
}

function sendTelemetry(telemetry: PedalTelemetry): void {
  lastTelemetry = telemetry;
  window?.webContents.send("telemetry", telemetry);
}

function setDemoEnabled(enabled: boolean): void {
  demoEnabled = enabled;
  if (demoTimer) clearInterval(demoTimer);
  demoTimer = null;

  if (enabled) {
    const startedAt = Date.now();
    demoTimer = setInterval(() => {
      const seconds = (Date.now() - startedAt) / 1000;
      const throttle = Math.max(0, Math.min(1, 0.64 + Math.sin(seconds * 1.7) * 0.36));
      const steering = Math.sin(seconds * 0.9) * 0.85;
      const brakePulse = Math.sin(seconds * 0.82);
      const brake = brakePulse > 0.63 ? Math.min(1, (brakePulse - 0.63) * 2.8) : 0;
      const speedKph = Math.round(Math.max(0, 110 + throttle * 210 - brake * 95));
      sendTelemetry({ speedKph, throttle, steering, brake, timestamp: Date.now() });
    }, 16);
  }

  window?.webContents.send("demo-changed", enabled);
  window?.webContents.send("status", enabled
    ? { state: "connected", message: "Demo signal", port: UDP_PORT }
    : lastStatus
  );
}

telemetryServer.on("telemetry", (telemetry) => {
  if (!demoEnabled) sendTelemetry(telemetry);
});

telemetryServer.on("status", (status) => {
  lastStatus = status;
  if (!demoEnabled) window?.webContents.send("status", status);
});

app.whenReady().then(() => {
  createWindow();
  telemetryServer.start();
  if (app.commandLine.hasSwitch("demo") || process.env.F1_OVERLAY_DEMO === "1") {
    setDemoEnabled(true);
  }
  globalShortcut.register("CommandOrControl+Shift+O", () => setLocked(!locked));
  globalShortcut.register("CommandOrControl+Shift+H", () => {
    if (!window) return;
    window.isVisible() ? window.hide() : window.showInactive();
  });
  globalShortcut.register("CommandOrControl+Shift+Q", () => app.quit());
  globalShortcut.register("CommandOrControl+Shift+D", () => setDemoEnabled(!demoEnabled));
  globalShortcut.register("CommandOrControl+Shift+S", () => setSteeringEnabled(!steeringEnabled));
});

ipcMain.on("set-locked", (_event, nextLocked: unknown) => {
  if (typeof nextLocked === "boolean") setLocked(nextLocked);
});
ipcMain.on("close-overlay", () => app.quit());
ipcMain.on("toggle-demo", () => setDemoEnabled(!demoEnabled));
ipcMain.handle("get-snapshot", (): OverlaySnapshot => ({
  telemetry: lastTelemetry,
  status: demoEnabled
    ? { state: "connected", message: "Demo signal", port: UDP_PORT }
    : lastStatus,
  locked,
  demoEnabled,
  steeringEnabled
}));

app.on("will-quit", () => {
  telemetryServer.stop();
  globalShortcut.unregisterAll();
  if (demoTimer) clearInterval(demoTimer);
});
app.on("window-all-closed", () => app.quit());
