import assert from "node:assert/strict";
import test from "node:test";
import {
  CAR_TELEMETRY_PACKET_ID,
  CAR_TELEMETRY_PACKET_SIZE,
  CAR_TELEMETRY_RECORD_SIZE,
  F1_25_PACKET_FORMAT,
  MOTION_EX_PACKET_ID,
  MOTION_EX_PACKET_SIZE,
  PACKET_HEADER_SIZE,
  WHEEL_SLIP_RATIO_OFFSET,
  parseF125Pedals,
  parseF125WheelMotion
} from "./parser";

test("parses the selected player car's throttle and brake", () => {
  const packet = Buffer.alloc(CAR_TELEMETRY_PACKET_SIZE);
  packet.writeUInt16LE(F1_25_PACKET_FORMAT, 0);
  packet.writeUInt8(CAR_TELEMETRY_PACKET_ID, 6);
  packet.writeUInt8(3, 27);

  const playerOffset = PACKET_HEADER_SIZE + 3 * CAR_TELEMETRY_RECORD_SIZE;
  packet.writeUInt16LE(287, playerOffset);
  packet.writeFloatLE(0.72, playerOffset + 2);
  packet.writeFloatLE(-0.45, playerOffset + 6);
  packet.writeFloatLE(0.31, playerOffset + 10);

  const result = parseF125Pedals(packet, 1234);
  assert.ok(result);
  assert.equal(result.timestamp, 1234);
  assert.equal(result.speedKph, 287);
  assert.ok(Math.abs(result.throttle - 0.72) < 0.0001);
  assert.ok(Math.abs(result.steering - -0.45) < 0.0001);
  assert.ok(Math.abs(result.brake - 0.31) < 0.0001);
  assert.equal(result.brakeLockup, "none");
});

test("parses wheel slip ratios from the motion ex packet", () => {
  const packet = Buffer.alloc(MOTION_EX_PACKET_SIZE);
  packet.writeUInt16LE(F1_25_PACKET_FORMAT, 0);
  packet.writeUInt8(MOTION_EX_PACKET_ID, 6);
  [-0.42, -0.08, 0.03, -0.51].forEach((value, wheelIndex) => {
    packet.writeFloatLE(value, WHEEL_SLIP_RATIO_OFFSET + wheelIndex * 4);
  });

  const result = parseF125WheelMotion(packet, 4321);
  assert.ok(result);
  assert.equal(result.timestamp, 4321);
  assert.ok(Math.abs(result.wheelSlipRatio[0] - -0.42) < 0.0001);
  assert.ok(Math.abs(result.wheelSlipRatio[3] - -0.51) < 0.0001);
});

test("ignores other packet types and malformed packets", () => {
  const packet = Buffer.alloc(CAR_TELEMETRY_PACKET_SIZE);
  packet.writeUInt16LE(F1_25_PACKET_FORMAT, 0);
  packet.writeUInt8(2, 6);
  packet.writeUInt8(0, 27);

  assert.equal(parseF125Pedals(packet), null);
  assert.equal(parseF125Pedals(Buffer.alloc(10)), null);
});

test("clamps invalid input values", () => {
  const packet = Buffer.alloc(CAR_TELEMETRY_PACKET_SIZE);
  packet.writeUInt16LE(F1_25_PACKET_FORMAT, 0);
  packet.writeUInt8(CAR_TELEMETRY_PACKET_ID, 6);
  packet.writeUInt8(0, 27);
  packet.writeFloatLE(1.5, PACKET_HEADER_SIZE + 2);
  packet.writeFloatLE(2, PACKET_HEADER_SIZE + 6);
  packet.writeFloatLE(-0.4, PACKET_HEADER_SIZE + 10);

  assert.deepEqual(parseF125Pedals(packet, 1), {
    speedKph: 0,
    throttle: 1,
    steering: 1,
    brake: 0,
    brakeLockup: "none",
    timestamp: 1
  });
});
