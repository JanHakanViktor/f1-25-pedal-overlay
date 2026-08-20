import { app, BrowserWindow, globalShortcut, ipcMain, screen } from "electron";
import path from "node:path";
import { TelemetryServer } from "./telemetry/server";

const requestedPort = Number.parseInt(process.env.F1_UDP_PORT ?? "20777", 10);
const UDP_PORT = Number.isInteger(requestedPort) && requestedPort > 0 && requestedPort <= 65535
  ? requestedPort
  : 20777;

const OVERLAY_WIDTH = 460;
const OVERLAY_HEIGHT = 180;

let window: BrowserWindow | null = null;
let locked = false;
const telemetryServer = new TelemetryServer(UDP_PORT);

function createWindow(): void {
  const workArea = screen.getPrimaryDisplay().workArea;

  window = new BrowserWindow({
    width: OVERLAY_WIDTH,
    height: OVERLAY_HEIGHT,
    x: workArea.x + workArea.width - OVERLAY_WIDTH - 40,
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
    window?.webContents.send("status", {
      state: "listening",
      message: `Waiting on UDP ${UDP_PORT}`,
      port: UDP_PORT
    });
  });
  window.on("closed", () => {
    window = null;
  });
}

function setLocked(nextLocked: boolean): void {
  locked = nextLocked;
  window?.setIgnoreMouseEvents(locked, { forward: true });
  window?.webContents.send("lock-changed", locked);
}

telemetryServer.on("telemetry", (telemetry) => {
  window?.webContents.send("telemetry", telemetry);
});

telemetryServer.on("status", (status) => {
  window?.webContents.send("status", status);
});

app.whenReady().then(() => {
  createWindow();
  telemetryServer.start();
  globalShortcut.register("CommandOrControl+Shift+O", () => setLocked(!locked));
  globalShortcut.register("CommandOrControl+Shift+H", () => {
    if (!window) return;
    window.isVisible() ? window.hide() : window.showInactive();
  });
  globalShortcut.register("CommandOrControl+Shift+Q", () => app.quit());
});

ipcMain.on("set-locked", (_event, nextLocked: unknown) => {
  if (typeof nextLocked === "boolean") setLocked(nextLocked);
});
ipcMain.on("close-overlay", () => app.quit());

app.on("will-quit", () => {
  telemetryServer.stop();
  globalShortcut.unregisterAll();
});
app.on("window-all-closed", () => app.quit());
