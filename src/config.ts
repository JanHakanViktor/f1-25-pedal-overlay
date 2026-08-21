import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import type { AppSettings } from "./shared";

export const DEFAULT_SETTINGS: AppSettings = {
  steeringEnabledByDefault: false,
  overlayTransparency: 0.3,
  udpPort: 20777,
  lockupSensitivity: 0.35,
  graphDurationSeconds: 5,
  shortcuts: {
    toggleVisibility: "Control+Shift+H",
    toggleLock: "Control+Shift+O",
    toggleDemo: "Control+Shift+D",
    toggleSteering: "Control+Shift+S",
    quit: "Control+Shift+Q"
  },
  lockupColorMode: "axle",
  lockupColors: {
    front: "#ffd84a",
    rear: "#ff8a2a",
    both: "#8f1525",
    single: "#ffd84a"
  }
};

export class SettingsStore {
  constructor(private readonly filePath: string) {}

  load(): AppSettings {
    try {
      const raw = JSON.parse(readFileSync(this.filePath, "utf8")) as unknown;
      return sanitizeSettings(raw);
    } catch {
      return structuredClone(DEFAULT_SETTINGS);
    }
  }

  save(value: unknown): AppSettings {
    const settings = sanitizeSettings(value);
    mkdirSync(path.dirname(this.filePath), { recursive: true });
    writeFileSync(this.filePath, `${JSON.stringify(settings, null, 2)}\n`, "utf8");
    return settings;
  }
}

export function sanitizeSettings(value: unknown): AppSettings {
  const input = isRecord(value) ? value : {};
  const shortcuts = isRecord(input.shortcuts) ? input.shortcuts : {};
  const lockupColors = isRecord(input.lockupColors) ? input.lockupColors : {};

  return {
    steeringEnabledByDefault: booleanValue(
      input.steeringEnabledByDefault,
      DEFAULT_SETTINGS.steeringEnabledByDefault
    ),
    overlayTransparency: numberValue(
      input.overlayTransparency,
      0.2,
      1,
      DEFAULT_SETTINGS.overlayTransparency
    ),
    udpPort: integerValue(input.udpPort, 1, 65535, DEFAULT_SETTINGS.udpPort),
    lockupSensitivity: numberValue(
      input.lockupSensitivity,
      0.15,
      0.9,
      DEFAULT_SETTINGS.lockupSensitivity
    ),
    graphDurationSeconds: numberValue(
      input.graphDurationSeconds,
      2,
      15,
      DEFAULT_SETTINGS.graphDurationSeconds
    ),
    shortcuts: {
      toggleVisibility: stringValue(shortcuts.toggleVisibility, DEFAULT_SETTINGS.shortcuts.toggleVisibility),
      toggleLock: stringValue(shortcuts.toggleLock, DEFAULT_SETTINGS.shortcuts.toggleLock),
      toggleDemo: stringValue(shortcuts.toggleDemo, DEFAULT_SETTINGS.shortcuts.toggleDemo),
      toggleSteering: stringValue(shortcuts.toggleSteering, DEFAULT_SETTINGS.shortcuts.toggleSteering),
      quit: stringValue(shortcuts.quit, DEFAULT_SETTINGS.shortcuts.quit)
    },
    lockupColorMode: input.lockupColorMode === "single" ? "single" : "axle",
    lockupColors: {
      front: colorValue(lockupColors.front, DEFAULT_SETTINGS.lockupColors.front),
      rear: colorValue(lockupColors.rear, DEFAULT_SETTINGS.lockupColors.rear),
      both: colorValue(lockupColors.both, DEFAULT_SETTINGS.lockupColors.both),
      single: colorValue(lockupColors.single, DEFAULT_SETTINGS.lockupColors.single)
    }
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function booleanValue(value: unknown, fallback: boolean): boolean {
  return typeof value === "boolean" ? value : fallback;
}

function numberValue(value: unknown, minimum: number, maximum: number, fallback: number): number {
  if (typeof value !== "number" || !Number.isFinite(value)) return fallback;
  return Math.min(maximum, Math.max(minimum, value));
}

function integerValue(value: unknown, minimum: number, maximum: number, fallback: number): number {
  if (typeof value !== "number" || !Number.isInteger(value)) return fallback;
  return Math.min(maximum, Math.max(minimum, value));
}

function stringValue(value: unknown, fallback: string): string {
  if (typeof value !== "string") return fallback;
  const trimmed = value.trim();
  return trimmed.length > 0 && trimmed.length <= 80 ? trimmed : fallback;
}

function colorValue(value: unknown, fallback: string): string {
  return typeof value === "string" && /^#[0-9a-f]{6}$/i.test(value) ? value.toLowerCase() : fallback;
}
