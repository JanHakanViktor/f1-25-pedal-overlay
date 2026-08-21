export type ConnectionState = "listening" | "connected" | "error";
export type BrakeLockup = "none" | "front" | "rear" | "both";
export type LockupColorMode = "axle" | "single";

export interface ShortcutSettings {
  toggleVisibility: string;
  toggleLock: string;
  toggleDemo: string;
  toggleSteering: string;
  quit: string;
}

export interface LockupColorSettings {
  front: string;
  rear: string;
  both: string;
  single: string;
}

export interface AppSettings {
  steeringEnabledByDefault: boolean;
  overlayTransparency: number;
  udpPort: number;
  lockupSensitivity: number;
  graphDurationSeconds: number;
  shortcuts: ShortcutSettings;
  lockupColorMode: LockupColorMode;
  lockupColors: LockupColorSettings;
}

export interface SaveSettingsResult {
  ok: boolean;
  settings?: AppSettings;
  error?: string;
}

export interface PedalTelemetry {
  speedKph: number;
  throttle: number;
  steering: number;
  brake: number;
  brakeLockup: BrakeLockup;
  timestamp: number;
}

export interface OverlayStatus {
  state: ConnectionState;
  message: string;
  port: number;
}

export interface OverlaySnapshot {
  telemetry: PedalTelemetry;
  status: OverlayStatus;
  locked: boolean;
  demoEnabled: boolean;
  steeringEnabled: boolean;
  settings: AppSettings;
}

export interface OverlayApi {
  onTelemetry: (callback: (telemetry: PedalTelemetry) => void) => () => void;
  onStatus: (callback: (status: OverlayStatus) => void) => () => void;
  onLockChanged: (callback: (locked: boolean) => void) => () => void;
  onDemoChanged: (callback: (enabled: boolean) => void) => () => void;
  getSnapshot: () => Promise<OverlaySnapshot>;
  startDrag: (screenX: number, screenY: number) => void;
  moveDrag: (screenX: number, screenY: number) => void;
  endDrag: () => void;
  showContextMenu: () => void;
  setLocked: (locked: boolean) => void;
  toggleDemo: () => void;
  close: () => void;
}

export interface SettingsApi {
  getSettings: () => Promise<AppSettings>;
  saveSettings: (settings: AppSettings) => Promise<SaveSettingsResult>;
  close: () => void;
}
