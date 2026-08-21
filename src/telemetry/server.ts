import dgram from "node:dgram";
import { EventEmitter } from "node:events";
import type { BrakeLockup, OverlayStatus, PedalTelemetry } from "../shared";
import { parseF125Pedals, parseF125WheelMotion } from "./parser";

const MOTION_MAX_AGE_MS = 150;
const DEFAULT_LOCKUP_SENSITIVITY = 0.35;

export interface TelemetryServerEvents {
  telemetry: [PedalTelemetry];
  status: [OverlayStatus];
}

export class TelemetryServer extends EventEmitter<TelemetryServerEvents> {
  private socket: dgram.Socket | null = null;
  private lastPacketAt = 0;
  private connectionTimer: NodeJS.Timeout | null = null;
  private wheelSlipRatio: [number, number, number, number] = [0, 0, 0, 0];
  private lastWheelMotionAt = 0;
  private frontBrakeLockup = false;
  private rearBrakeLockup = false;
  private lockupSensitivity = DEFAULT_LOCKUP_SENSITIVITY;

  constructor(private readonly port = 20777) {
    super();
  }

  setLockupSensitivity(value: number): void {
    if (!Number.isFinite(value)) return;
    this.lockupSensitivity = Math.min(0.9, Math.max(0.15, value));
  }

  start(): void {
    if (this.socket) return;

    // Do not share the port silently. On Windows a second telemetry app can
    // otherwise bind successfully while another process receives the packets.
    this.socket = dgram.createSocket("udp4");
    this.socket.on("message", (packet) => {
      const wheelMotion = parseF125WheelMotion(packet);
      if (wheelMotion) {
        this.wheelSlipRatio = wheelMotion.wheelSlipRatio;
        this.lastWheelMotionAt = wheelMotion.timestamp;
        return;
      }

      const telemetry = parseF125Pedals(packet);
      if (!telemetry) return;

      telemetry.brakeLockup = this.detectBrakeLockup(telemetry);

      const wasDisconnected = Date.now() - this.lastPacketAt > 1500;
      this.lastPacketAt = Date.now();
      if (wasDisconnected) {
        this.emitStatus("connected", "F1 25 connected");
      }
      this.emit("telemetry", telemetry);
    });

    this.socket.on("error", (error) => {
      const message = (error as NodeJS.ErrnoException).code === "EADDRINUSE"
        ? `UDP ${this.port} is already in use`
        : error.message;
      this.emitStatus("error", message);
    });

    this.socket.bind(this.port, "0.0.0.0", () => {
      this.emitStatus("listening", `Waiting on UDP ${this.port}`);
    });

    this.connectionTimer = setInterval(() => {
      if (this.lastPacketAt > 0 && Date.now() - this.lastPacketAt > 1500) {
        this.lastPacketAt = 0;
        this.emitStatus("listening", `Waiting on UDP ${this.port}`);
      }
    }, 500);
  }

  stop(): void {
    if (this.connectionTimer) clearInterval(this.connectionTimer);
    this.connectionTimer = null;
    try {
      this.socket?.close();
    } catch {
      // A failed bind leaves the socket in an already-closed state.
    }
    this.socket = null;
    this.wheelSlipRatio = [0, 0, 0, 0];
    this.lastWheelMotionAt = 0;
    this.frontBrakeLockup = false;
    this.rearBrakeLockup = false;
  }

  private detectBrakeLockup(telemetry: PedalTelemetry): BrakeLockup {
    const motionIsFresh = telemetry.timestamp - this.lastWheelMotionAt <= MOTION_MAX_AGE_MS;
    const isBrakingAtSpeed = telemetry.brake >= 0.1 && telemetry.speedKph >= 20;
    if (!motionIsFresh || !isBrakingAtSpeed) {
      this.frontBrakeLockup = false;
      this.rearBrakeLockup = false;
      return "none";
    }

    // F1 wheel arrays are ordered rear-left, rear-right, front-left, front-right.
    const rearSlipRatio = Math.min(this.wheelSlipRatio[0], this.wheelSlipRatio[1]);
    const frontSlipRatio = Math.min(this.wheelSlipRatio[2], this.wheelSlipRatio[3]);
    this.frontBrakeLockup = this.axleIsLocked(frontSlipRatio, this.frontBrakeLockup);
    this.rearBrakeLockup = this.axleIsLocked(rearSlipRatio, this.rearBrakeLockup);

    if (this.frontBrakeLockup && this.rearBrakeLockup) return "both";
    if (this.frontBrakeLockup) return "front";
    if (this.rearBrakeLockup) return "rear";
    return "none";
  }

  private axleIsLocked(slipRatio: number, wasLocked: boolean): boolean {
    const enterThreshold = -this.lockupSensitivity;
    const exitThreshold = -Math.max(0.08, this.lockupSensitivity * 0.52);
    const threshold = wasLocked ? exitThreshold : enterThreshold;
    return slipRatio <= threshold;
  }

  private emitStatus(state: OverlayStatus["state"], message: string): void {
    this.emit("status", { state, message, port: this.port });
  }
}
