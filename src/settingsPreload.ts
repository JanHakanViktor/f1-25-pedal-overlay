import { contextBridge, ipcRenderer } from "electron";
import type { AppSettings, SaveSettingsResult, SettingsApi } from "./shared";

const api: SettingsApi = {
  getSettings: () => ipcRenderer.invoke("settings:get") as Promise<AppSettings>,
  saveSettings: (settings) => ipcRenderer.invoke("settings:save", settings) as Promise<SaveSettingsResult>,
  close: () => ipcRenderer.send("settings:close")
};

contextBridge.exposeInMainWorld("settings", api);

declare global {
  interface Window {
    settings: SettingsApi;
  }
}
