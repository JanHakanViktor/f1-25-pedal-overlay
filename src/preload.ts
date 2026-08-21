import { contextBridge, ipcRenderer } from "electron";
import type { OverlayApi, OverlaySnapshot, OverlayStatus, PedalTelemetry } from "./shared";

function subscribe<T>(channel: string, callback: (value: T) => void): () => void {
  const listener = (_event: Electron.IpcRendererEvent, value: T) => callback(value);
  ipcRenderer.on(channel, listener);
  return () => ipcRenderer.removeListener(channel, listener);
}

const api: OverlayApi = {
  onTelemetry: (callback) => subscribe<PedalTelemetry>("telemetry", callback),
  onStatus: (callback) => subscribe<OverlayStatus>("status", callback),
  onLockChanged: (callback) => subscribe<boolean>("lock-changed", callback),
  onDemoChanged: (callback) => subscribe<boolean>("demo-changed", callback),
  getSnapshot: () => ipcRenderer.invoke("get-snapshot") as Promise<OverlaySnapshot>,
  startDrag: (screenX, screenY) => ipcRenderer.send("overlay:drag-start", { screenX, screenY }),
  moveDrag: (screenX, screenY) => ipcRenderer.send("overlay:drag-move", { screenX, screenY }),
  endDrag: () => ipcRenderer.send("overlay:drag-end"),
  showContextMenu: () => ipcRenderer.send("overlay:show-context-menu"),
  setLocked: (locked) => ipcRenderer.send("set-locked", locked),
  toggleDemo: () => ipcRenderer.send("toggle-demo"),
  close: () => ipcRenderer.send("close-overlay")
};

contextBridge.exposeInMainWorld("overlay", api);

declare global {
  interface Window {
    overlay: OverlayApi;
  }
}
