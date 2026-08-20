import dgram from "node:dgram";
import { EventEmitter } from "node:events";
import type { OverlayStatus, PedalTelemetry } from "../shared";
import { parseF125Pedals } from "./parser";

export interface TelemetryServerEvents {
  telemetry: [PedalTelemetry];
  status: [OverlayStatus];
}

export class TelemetryServer extends EventEmitter<TelemetryServerEvents> {
  private socket: dgram.Socket | null = null;
  private lastPacketAt = 0;
  private connectionTimer: NodeJS.Timeout | null = null;

  constructor(private readonly port = 20777) {
    super();
  }

  start(): void {
    if (this.socket) return;

    // Do not share the port silently. On Windows a second telemetry app can
    // otherwise bind successfully while another process receives the packets.
    this.socket = dgram.createSocket("udp4");
    this.socket.on("message", (packet) => {
      const telemetry = parseF125Pedals(packet);
      if (!telemetry) return;

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
  }

  private emitStatus(state: OverlayStatus["state"], message: string): void {
    this.emit("status", { state, message, port: this.port });
  }
}
