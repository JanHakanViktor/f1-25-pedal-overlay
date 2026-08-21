import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { DEFAULT_SETTINGS, sanitizeSettings, SettingsStore } from "./config";

test("uses safe defaults for invalid settings", () => {
  assert.deepEqual(sanitizeSettings(null), DEFAULT_SETTINGS);
  assert.deepEqual(sanitizeSettings({ udpPort: "20777", lockupColors: { front: "yellow" } }), DEFAULT_SETTINGS);
});

test("normalizes configurable ranges and colours", () => {
  const result = sanitizeSettings({
    steeringEnabledByDefault: true,
    overlayTransparency: 0.05,
    udpPort: 20778,
    lockupSensitivity: 2,
    graphDurationSeconds: 8.5,
    lockupColorMode: "single",
    lockupColors: { front: "#AABBCC", single: "#123456" }
  });

  assert.equal(result.steeringEnabledByDefault, true);
  assert.equal(result.overlayTransparency, 0.2);
  assert.equal(result.udpPort, 20778);
  assert.equal(result.lockupSensitivity, 0.9);
  assert.equal(result.graphDurationSeconds, 8.5);
  assert.equal(result.lockupColorMode, "single");
  assert.equal(result.lockupColors.front, "#aabbcc");
  assert.equal(result.lockupColors.single, "#123456");
});

test("persists settings between application launches", (context) => {
  const directory = mkdtempSync(path.join(tmpdir(), "f1-overlay-settings-"));
  context.after(() => rmSync(directory, { recursive: true, force: true }));

  const store = new SettingsStore(path.join(directory, "settings.json"));
  store.save({
    ...DEFAULT_SETTINGS,
    steeringEnabledByDefault: true,
    udpPort: 20778,
    graphDurationSeconds: 7
  });

  const loaded = store.load();
  assert.equal(loaded.steeringEnabledByDefault, true);
  assert.equal(loaded.udpPort, 20778);
  assert.equal(loaded.graphDurationSeconds, 7);
});
