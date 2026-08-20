export type ConnectionState = "listening" | "connected" | "error";

export interface PedalTelemetry {
  speedKph: number;
  throttle: number;
  steering: number;
  brake: number;
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
}

export interface OverlayApi {
  onTelemetry: (callback: (telemetry: PedalTelemetry) => void) => () => void;
  onStatus: (callback: (status: OverlayStatus) => void) => () => void;
  onLockChanged: (callback: (locked: boolean) => void) => () => void;
  onDemoChanged: (callback: (enabled: boolean) => void) => () => void;
  getSnapshot: () => Promise<OverlaySnapshot>;
  setLocked: (locked: boolean) => void;
  toggleDemo: () => void;
  close: () => void;
}
