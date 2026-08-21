import {
  app,
  BrowserWindow,
  globalShortcut,
  ipcMain,
  Menu,
  screen,
  Tray
} from "electron";
import squirrelStartup from "electron-squirrel-startup";
import path from "node:path";
import { DEFAULT_SETTINGS, sanitizeSettings, SettingsStore } from "./config";
import type {
  AppSettings,
  OverlaySnapshot,
  OverlayStatus,
  PedalTelemetry,
  SaveSettingsResult,
  ShortcutSettings
} from "./shared";
import { TelemetryServer } from "./telemetry/server";

if (squirrelStartup) {
  app.quit();
}

const OVERLAY_WIDTH = 460;
const STEERING_GAUGE_WIDTH = 141;
const OVERLAY_HEIGHT = 150;

let overlayWindow: BrowserWindow | null = null;
let settingsWindow: BrowserWindow | null = null;
let tray: Tray | null = null;
let settingsStore: SettingsStore | null = null;
let settings: AppSettings = structuredClone(DEFAULT_SETTINGS);
let telemetryServer: TelemetryServer | null = null;
let udpPort = DEFAULT_SETTINGS.udpPort;
let locked = false;
let demoEnabled = false;
let steeringEnabled = false;
let demoTimer: NodeJS.Timeout | null = null;
let lastTelemetry: PedalTelemetry = emptyTelemetry();
let lastStatus: OverlayStatus = waitingStatus(udpPort);

function createOverlayWindow(): void {
  const workArea = screen.getPrimaryDisplay().workArea;
  const windowWidth = OVERLAY_WIDTH + (steeringEnabled ? STEERING_GAUGE_WIDTH : 0);

  overlayWindow = new BrowserWindow({
    width: windowWidth,
    height: OVERLAY_HEIGHT,
    x: workArea.x + workArea.width - windowWidth - 40,
    y: workArea.y + Math.round((workArea.height - OVERLAY_HEIGHT) / 2),
    transparent: true,
    frame: false,
    skipTaskbar: true,
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

  overlayWindow.setAlwaysOnTop(true, "screen-saver");
  overlayWindow.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });
  void overlayWindow.loadFile(path.join(__dirname, "renderer", "index.html"));
  overlayWindow.webContents.on("did-finish-load", () => {
    overlayWindow?.webContents.send("lock-changed", locked);
    overlayWindow?.webContents.send("demo-changed", demoEnabled);
    overlayWindow?.webContents.send("steering-changed", steeringEnabled);
    overlayWindow?.webContents.send("status", currentStatus());
  });
  overlayWindow.on("show", refreshTrayMenu);
  overlayWindow.on("hide", refreshTrayMenu);
  overlayWindow.on("closed", () => {
    overlayWindow = null;
    refreshTrayMenu();
  });
}

function openSettingsWindow(): void {
  if (settingsWindow) {
    settingsWindow.show();
    settingsWindow.focus();
    return;
  }

  const workArea = screen.getPrimaryDisplay().workArea;
  settingsWindow = new BrowserWindow({
    width: 700,
    height: Math.min(820, workArea.height - 60),
    minWidth: 540,
    minHeight: 620,
    title: "F1 25 Pedal Overlay Settings",
    autoHideMenuBar: true,
    backgroundColor: "#090b0e",
    show: false,
    webPreferences: {
      preload: path.join(__dirname, "settingsPreload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true
    }
  });

  void settingsWindow.loadFile(path.join(__dirname, "settings", "index.html"));
  settingsWindow.once("ready-to-show", () => settingsWindow?.show());
  settingsWindow.on("closed", () => {
    settingsWindow = null;
  });
}

async function createTray(): Promise<void> {
  const icon = await app.getFileIcon(process.execPath, { size: "small" });
  tray = new Tray(icon);
  tray.setToolTip("F1 25 Pedal Overlay");
  refreshTrayMenu();
}

function refreshTrayMenu(): void {
  if (!tray) return;

  const overlayVisible = overlayWindow?.isVisible() ?? false;
  tray.setContextMenu(Menu.buildFromTemplate([
    { label: "Settings", click: openSettingsWindow },
    {
      label: overlayVisible ? "Hide overlay" : "Show overlay",
      accelerator: settings.shortcuts.toggleVisibility,
      click: toggleOverlayVisibility
    },
    {
      label: locked ? "Unlock position" : "Lock position",
      accelerator: settings.shortcuts.toggleLock,
      click: () => setLocked(!locked)
    },
    {
      label: "Enable steering",
      type: "checkbox",
      checked: steeringEnabled,
      accelerator: settings.shortcuts.toggleSteering,
      click: (item) => setSteeringEnabled(item.checked)
    },
    { type: "separator" },
    { label: "Exit", accelerator: settings.shortcuts.quit, click: () => app.quit() }
  ]));
}

function toggleOverlayVisibility(): void {
  if (!overlayWindow) {
    createOverlayWindow();
    return;
  }
  overlayWindow.isVisible() ? overlayWindow.hide() : overlayWindow.showInactive();
  refreshTrayMenu();
}

function setSteeringEnabled(enabled: boolean): void {
  if (steeringEnabled === enabled) return;
  steeringEnabled = enabled;

  if (overlayWindow) {
    const bounds = overlayWindow.getBounds();
    const nextWidth = OVERLAY_WIDTH + (steeringEnabled ? STEERING_GAUGE_WIDTH : 0);
    const widthDelta = nextWidth - bounds.width;
    overlayWindow.setResizable(true);
    overlayWindow.setBounds({
      x: bounds.x - widthDelta,
      y: bounds.y,
      width: nextWidth,
      height: OVERLAY_HEIGHT
    });
    overlayWindow.setResizable(false);
    overlayWindow.webContents.send("steering-changed", steeringEnabled);
  }
  refreshTrayMenu();
}

function setLocked(nextLocked: boolean): void {
  locked = nextLocked;
  overlayWindow?.setIgnoreMouseEvents(locked, { forward: true });
  overlayWindow?.webContents.send("lock-changed", locked);
  refreshTrayMenu();
}

function sendTelemetry(telemetry: PedalTelemetry): void {
  lastTelemetry = telemetry;
  overlayWindow?.webContents.send("telemetry", telemetry);
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
      const lockupPulse = Math.sin(seconds * 5.5);
      const brakeLockup = brake <= 0.72
        ? "none"
        : lockupPulse > 0.35
          ? "front"
          : lockupPulse < -0.35
            ? "rear"
            : "both";
      const speedKph = Math.round(Math.max(0, 110 + throttle * 210 - brake * 95));
      sendTelemetry({ speedKph, throttle, steering, brake, brakeLockup, timestamp: Date.now() });
    }, 16);
  }

  overlayWindow?.webContents.send("demo-changed", enabled);
  overlayWindow?.webContents.send("status", currentStatus());
}

function startTelemetryServer(nextPort: number): void {
  telemetryServer?.stop();
  udpPort = nextPort;
  lastStatus = waitingStatus(udpPort);

  const server = new TelemetryServer(udpPort);
  server.setLockupSensitivity(settings.lockupSensitivity);
  server.on("telemetry", (telemetry) => {
    if (!demoEnabled) sendTelemetry(telemetry);
  });
  server.on("status", (status) => {
    lastStatus = status;
    if (!demoEnabled) overlayWindow?.webContents.send("status", status);
  });
  telemetryServer = server;
  server.start();
}

function registerShortcutSet(shortcuts: ShortcutSettings): string | null {
  const entries: Array<[string, () => void]> = [
    [shortcuts.toggleVisibility, toggleOverlayVisibility],
    [shortcuts.toggleLock, () => setLocked(!locked)],
    [shortcuts.toggleDemo, () => setDemoEnabled(!demoEnabled)],
    [shortcuts.toggleSteering, () => setSteeringEnabled(!steeringEnabled)],
    [shortcuts.quit, () => app.quit()]
  ];

  const normalized = entries.map(([accelerator]) => accelerator.replaceAll(" ", "").toLowerCase());
  if (new Set(normalized).size !== normalized.length) return "Every shortcut must be unique.";

  globalShortcut.unregisterAll();
  try {
    for (const [accelerator, callback] of entries) {
      if (!globalShortcut.register(accelerator, callback)) {
        globalShortcut.unregisterAll();
        return `The shortcut ${accelerator} is already used by another application.`;
      }
    }
  } catch {
    globalShortcut.unregisterAll();
    return "One or more shortcuts use an invalid format.";
  }
  return null;
}

function saveSettings(value: unknown): SaveSettingsResult {
  if (!settingsStore) return { ok: false, error: "Settings are not ready yet." };

  const candidate = sanitizeSettings(value);
  const shortcutError = registerShortcutSet(candidate.shortcuts);
  if (shortcutError) {
    registerShortcutSet(settings.shortcuts);
    return { ok: false, error: shortcutError };
  }

  const previousSettings = settings;
  try {
    settings = settingsStore.save(candidate);
  } catch {
    registerShortcutSet(previousSettings.shortcuts);
    return { ok: false, error: "Windows could not write the settings file." };
  }

  telemetryServer?.setLockupSensitivity(settings.lockupSensitivity);
  if (settings.udpPort !== udpPort) startTelemetryServer(settings.udpPort);
  refreshTrayMenu();
  return { ok: true, settings };
}

function resolveUdpPort(): number {
  const portArgument = app.commandLine.getSwitchValue("udp-port")
    || process.argv.find((argument) => argument.startsWith("--udp-port="))?.split("=")[1];
  const requested = Number.parseInt(portArgument ?? process.env.F1_UDP_PORT ?? "", 10);
  return Number.isInteger(requested) && requested > 0 && requested <= 65535
    ? requested
    : settings.udpPort;
}

function currentStatus(): OverlayStatus {
  return demoEnabled
    ? { state: "connected", message: "Demo signal", port: udpPort }
    : lastStatus;
}

function waitingStatus(port: number): OverlayStatus {
  return { state: "listening", message: `Waiting on UDP ${port}`, port };
}

function emptyTelemetry(): PedalTelemetry {
  return {
    speedKph: 0,
    throttle: 0,
    steering: 0,
    brake: 0,
    brakeLockup: "none",
    timestamp: 0
  };
}

ipcMain.on("set-locked", (_event, nextLocked: unknown) => {
  if (typeof nextLocked === "boolean") setLocked(nextLocked);
});
ipcMain.on("close-overlay", () => app.quit());
ipcMain.on("toggle-demo", () => setDemoEnabled(!demoEnabled));
ipcMain.on("settings:close", () => settingsWindow?.close());
ipcMain.handle("settings:get", () => settings);
ipcMain.handle("settings:save", (_event, value: unknown) => saveSettings(value));
ipcMain.handle("get-snapshot", (): OverlaySnapshot => ({
  telemetry: lastTelemetry,
  status: currentStatus(),
  locked,
  demoEnabled,
  steeringEnabled,
  settings
}));

const hasSingleInstanceLock = app.requestSingleInstanceLock();
if (!hasSingleInstanceLock) {
  app.quit();
} else {
  app.on("second-instance", () => {
    if (!overlayWindow) createOverlayWindow();
    overlayWindow?.showInactive();
  });

  app.whenReady().then(async () => {
    app.setAppUserModelId("com.squirrel.F125PedalOverlay.F125PedalOverlay");
    settingsStore = new SettingsStore(path.join(app.getPath("userData"), "settings.json"));
    settings = settingsStore.load();
    udpPort = resolveUdpPort();
    steeringEnabled = app.commandLine.hasSwitch("steering")
      || process.argv.includes("--steering")
      || process.env.F1_OVERLAY_STEERING === "1"
      || settings.steeringEnabledByDefault;

    createOverlayWindow();
    await createTray();
    startTelemetryServer(udpPort);

    const shortcutError = registerShortcutSet(settings.shortcuts);
    if (shortcutError) {
      settings = settingsStore.save({ ...settings, shortcuts: DEFAULT_SETTINGS.shortcuts });
      registerShortcutSet(settings.shortcuts);
    }

    if (app.commandLine.hasSwitch("demo") || process.env.F1_OVERLAY_DEMO === "1") {
      setDemoEnabled(true);
    }
  });
}

app.on("activate", () => {
  if (!overlayWindow) createOverlayWindow();
  overlayWindow?.showInactive();
});

app.on("will-quit", () => {
  telemetryServer?.stop();
  globalShortcut.unregisterAll();
  if (demoTimer) clearInterval(demoTimer);
  tray?.destroy();
});
