import type { PedalTelemetry } from "../shared";

export const F1_25_PACKET_FORMAT = 2025;
export const PACKET_HEADER_SIZE = 29;
export const CAR_TELEMETRY_PACKET_ID = 6;
export const CAR_TELEMETRY_RECORD_SIZE = 60;
export const MAX_CARS = 22;
export const CAR_TELEMETRY_PACKET_SIZE = 1352;

export function parseF125Pedals(packet: Buffer, timestamp = Date.now()): PedalTelemetry | null {
  if (packet.length < PACKET_HEADER_SIZE) return null;

  const packetFormat = packet.readUInt16LE(0);
  const packetId = packet.readUInt8(6);
  const playerCarIndex = packet.readUInt8(27);

  if (
    packetFormat !== F1_25_PACKET_FORMAT ||
    packetId !== CAR_TELEMETRY_PACKET_ID ||
    playerCarIndex >= MAX_CARS
  ) {
    return null;
  }

  const recordOffset = PACKET_HEADER_SIZE + playerCarIndex * CAR_TELEMETRY_RECORD_SIZE;
  const brakeOffset = recordOffset + 10;

  if (packet.length < brakeOffset + 4) return null;

  return {
    speedKph: packet.readUInt16LE(recordOffset),
    throttle: clampInput(packet.readFloatLE(recordOffset + 2)),
    steering: clampSteering(packet.readFloatLE(recordOffset + 6)),
    brake: clampInput(packet.readFloatLE(brakeOffset)),
    timestamp
  };
}

function clampSteering(value: number): number {
  if (!Number.isFinite(value)) return 0;
  return Math.min(1, Math.max(-1, value));
}

function clampInput(value: number): number {
  if (!Number.isFinite(value)) return 0;
  return Math.min(1, Math.max(0, value));
}
