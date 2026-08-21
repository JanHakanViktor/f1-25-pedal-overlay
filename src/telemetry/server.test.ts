import assert from "node:assert/strict";
import dgram from "node:dgram";
import test from "node:test";
import { once } from "node:events";
import {
  CAR_TELEMETRY_PACKET_SIZE,
  F1_25_PACKET_FORMAT,
  MOTION_EX_PACKET_ID,
  MOTION_EX_PACKET_SIZE,
  PACKET_HEADER_SIZE,
  WHEEL_SLIP_RATIO_OFFSET
} from "./parser";
import { TelemetryServer } from "./server";

test("receives and parses an F1 25 telemetry datagram", async (context) => {
  const port = 30777;
  const server = new TelemetryServer(port);
  context.after(() => server.stop());

  const listening = once(server, "status");
  server.start();
  const [status] = await listening;
  assert.equal(status.state, "listening");

  const packet = Buffer.alloc(CAR_TELEMETRY_PACKET_SIZE);
  packet.writeUInt16LE(F1_25_PACKET_FORMAT, 0);
  packet.writeUInt8(6, 6);
  packet.writeUInt8(0, 27);
  packet.writeUInt16LE(301, PACKET_HEADER_SIZE);
  packet.writeFloatLE(0.72, PACKET_HEADER_SIZE + 2);
  packet.writeFloatLE(-0.25, PACKET_HEADER_SIZE + 6);
  packet.writeFloatLE(0.31, PACKET_HEADER_SIZE + 10);

  const received = once(server, "telemetry");
  const sender = dgram.createSocket("udp4");
  context.after(() => sender.close());
  await new Promise<void>((resolve, reject) => {
    sender.send(packet, port, "127.0.0.1", (error) => error ? reject(error) : resolve());
  });

  const [telemetry] = await received;
  assert.equal(telemetry.speedKph, 301);
  assert.ok(Math.abs(telemetry.throttle - 0.72) < 0.0001);
  assert.ok(Math.abs(telemetry.steering - -0.25) < 0.0001);
  assert.ok(Math.abs(telemetry.brake - 0.31) < 0.0001);
  assert.equal(telemetry.brakeLockup, "none");

  const wheelMotionPacket = Buffer.alloc(MOTION_EX_PACKET_SIZE);
  wheelMotionPacket.writeUInt16LE(F1_25_PACKET_FORMAT, 0);
  wheelMotionPacket.writeUInt8(MOTION_EX_PACKET_ID, 6);
  wheelMotionPacket.writeFloatLE(-0.62, WHEEL_SLIP_RATIO_OFFSET + 2 * 4);

  const lockupReceived = once(server, "telemetry");
  await new Promise<void>((resolve, reject) => {
    sender.send(wheelMotionPacket, port, "127.0.0.1", (error) => error ? reject(error) : resolve());
  });
  packet.writeUInt16LE(185, PACKET_HEADER_SIZE);
  packet.writeFloatLE(0.82, PACKET_HEADER_SIZE + 10);
  await new Promise<void>((resolve, reject) => {
    sender.send(packet, port, "127.0.0.1", (error) => error ? reject(error) : resolve());
  });

  const [lockupTelemetry] = await lockupReceived;
  assert.equal(lockupTelemetry.brakeLockup, "front");

  wheelMotionPacket.writeFloatLE(-0.62, WHEEL_SLIP_RATIO_OFFSET);
  wheelMotionPacket.writeFloatLE(0, WHEEL_SLIP_RATIO_OFFSET + 2 * 4);
  const rearLockupReceived = once(server, "telemetry");
  await new Promise<void>((resolve, reject) => {
    sender.send(wheelMotionPacket, port, "127.0.0.1", (error) => error ? reject(error) : resolve());
  });
  await new Promise<void>((resolve, reject) => {
    sender.send(packet, port, "127.0.0.1", (error) => error ? reject(error) : resolve());
  });
  const [rearLockupTelemetry] = await rearLockupReceived;
  assert.equal(rearLockupTelemetry.brakeLockup, "rear");

  wheelMotionPacket.writeFloatLE(-0.62, WHEEL_SLIP_RATIO_OFFSET + 2 * 4);
  const bothLockupReceived = once(server, "telemetry");
  await new Promise<void>((resolve, reject) => {
    sender.send(wheelMotionPacket, port, "127.0.0.1", (error) => error ? reject(error) : resolve());
  });
  await new Promise<void>((resolve, reject) => {
    sender.send(packet, port, "127.0.0.1", (error) => error ? reject(error) : resolve());
  });
  const [bothLockupTelemetry] = await bothLockupReceived;
  assert.equal(bothLockupTelemetry.brakeLockup, "both");
});
