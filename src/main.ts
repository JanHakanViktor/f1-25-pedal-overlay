import { app, BrowserWindow, screen } from "electron";
import path from "node:path";

const OVERLAY_WIDTH = 460;
const OVERLAY_HEIGHT = 180;

let window: BrowserWindow | null = null;

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
  window.on("closed", () => {
    window = null;
  });
}

app.whenReady().then(createWindow);
app.on("window-all-closed", () => app.quit());
