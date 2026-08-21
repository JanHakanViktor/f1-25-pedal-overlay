const throttleFill = getElement<HTMLDivElement>("throttleFill");
const brakeFill = getElement<HTMLDivElement>("brakeFill");
const overlayElement = getElement<HTMLElement>("overlay");
const steeringGauge = getElement<HTMLElement>("steeringGauge");
const steeringDial = getElement<HTMLElement>("steeringDial");
const steeringMarker = getElement<HTMLDivElement>("steeringMarker");
const steeringValue = getElement<HTMLOutputElement>("steeringValue");
const historyCanvas = getElement<HTMLCanvasElement>("historyCanvas");
const historyContext = historyCanvas.getContext("2d");

type BrakeLockup = "none" | "front" | "rear" | "both";

interface HistorySample {
  time: number;
  throttle: number;
  brake: number;
  brakeLockup: BrakeLockup;
}

const SAMPLE_INTERVAL_MS = 40;
const MAX_STEERING_DEGREES = 180;
const STEERING_MARKER_ARC_DEGREES = 90;
const THROTTLE_COLOR = "#42e37c";
const BRAKE_COLOR = "#ff4261";
const inputHistory: HistorySample[] = [];

let historyDurationMs = 5000;
let lockupColorMode: "axle" | "single" = "axle";
let frontLockupColor = "#ffd84a";
let rearLockupColor = "#ff8a2a";
let bothLockupColor = "#8f1525";
let singleLockupColor = "#ffd84a";

let shownThrottle = 0;
let shownBrake = 0;
let shownSteering = 0;
let targetThrottle = 0;
let targetBrake = 0;
let targetSteering = 0;
let targetBrakeLockup: BrakeLockup = "none";
let lastSampleAt = -SAMPLE_INTERVAL_MS;
let snapshotPending = false;
let locked = false;
let dragPointerId: number | null = null;

window.overlay.onTelemetry((telemetry) => {
  targetThrottle = telemetry.throttle;
  targetBrake = telemetry.brake;
  targetSteering = telemetry.steering;
  targetBrakeLockup = telemetry.brakeLockup;
});

window.overlay.onLockChanged((nextLocked) => {
  applyLockedState(nextLocked);
});

overlayElement.addEventListener("pointerdown", (event) => {
  if (event.button !== 0 || locked) return;
  dragPointerId = event.pointerId;
  overlayElement.setPointerCapture(event.pointerId);
  window.overlay.startDrag(event.screenX, event.screenY);
  event.preventDefault();
});

overlayElement.addEventListener("pointermove", (event) => {
  if (event.pointerId !== dragPointerId) return;
  window.overlay.moveDrag(event.screenX, event.screenY);
});

overlayElement.addEventListener("pointerup", finishOverlayDrag);
overlayElement.addEventListener("pointercancel", finishOverlayDrag);
overlayElement.addEventListener("contextmenu", (event) => {
  event.preventDefault();
  window.overlay.showContextMenu();
});
window.addEventListener("blur", () => finishOverlayDrag());

setInterval(() => void refreshSnapshot(), 50);
void refreshSnapshot();

async function refreshSnapshot(): Promise<void> {
  if (snapshotPending) return;
  snapshotPending = true;
  try {
    const snapshot = await window.overlay.getSnapshot();
    targetThrottle = snapshot.telemetry.throttle;
    targetBrake = snapshot.telemetry.brake;
    targetSteering = snapshot.telemetry.steering;
    targetBrakeLockup = snapshot.telemetry.brakeLockup;
    applySettings(snapshot.settings);
    applyLockedState(snapshot.locked);
    overlayElement.classList.toggle("has-steering", snapshot.steeringEnabled);
    steeringGauge.hidden = !snapshot.steeringEnabled;
  } catch {
    // The main process may be closing while a scheduled refresh is in flight.
  } finally {
    snapshotPending = false;
  }
}

function applyLockedState(nextLocked: boolean): void {
  locked = nextLocked;
  overlayElement.classList.toggle("is-locked", locked);
  if (locked) finishOverlayDrag();
}

function finishOverlayDrag(event?: PointerEvent): void {
  if (dragPointerId === null || (event && event.pointerId !== dragPointerId)) return;
  if (overlayElement.hasPointerCapture(dragPointerId)) {
    overlayElement.releasePointerCapture(dragPointerId);
  }
  dragPointerId = null;
  window.overlay.endDrag();
}

function render(now: number): void {
  // A small amount of smoothing prevents visible UDP stair-stepping without adding lag.
  shownThrottle += (targetThrottle - shownThrottle) * 0.42;
  shownBrake += (targetBrake - shownBrake) * 0.42;
  shownSteering += (targetSteering - shownSteering) * 0.35;

  setMeter(throttleFill, shownThrottle);
  setMeter(brakeFill, shownBrake);
  setSteering(shownSteering);

  if (now - lastSampleAt >= SAMPLE_INTERVAL_MS) {
    inputHistory.push({
      time: now,
      throttle: shownThrottle,
      brake: shownBrake,
      brakeLockup: targetBrakeLockup
    });
    lastSampleAt = now;
  }

  const cutoff = now - historyDurationMs;
  while (inputHistory.length > 1 && inputHistory[1].time < cutoff) inputHistory.shift();
  drawHistory(now);
  requestAnimationFrame(render);
}

function setSteering(input: number): void {
  const normalized = Math.min(1, Math.max(-1, input));
  const degrees = Math.round(normalized * MAX_STEERING_DEGREES);
  const markerAngle = normalized * STEERING_MARKER_ARC_DEGREES;

  steeringMarker.style.setProperty("--steer-angle", `${markerAngle}deg`);
  steeringValue.textContent = `${degrees}°`;
  steeringDial.setAttribute("aria-valuenow", String(degrees));
}

function drawHistory(now: number): void {
  if (!historyContext) return;

  const deviceScale = window.devicePixelRatio || 1;
  const width = historyCanvas.clientWidth;
  const height = historyCanvas.clientHeight;
  const pixelWidth = Math.round(width * deviceScale);
  const pixelHeight = Math.round(height * deviceScale);

  if (historyCanvas.width !== pixelWidth || historyCanvas.height !== pixelHeight) {
    historyCanvas.width = pixelWidth;
    historyCanvas.height = pixelHeight;
  }

  historyContext.setTransform(deviceScale, 0, 0, deviceScale, 0, 0);
  historyContext.clearRect(0, 0, width, height);
  drawSignal("throttle", THROTTLE_COLOR, now, width, height);
  drawBrakeSignal(now, width, height);
}

function drawSignal(
  input: "throttle" | "brake",
  color: string,
  now: number,
  width: number,
  height: number
): void {
  if (!historyContext || inputHistory.length === 0) return;

  const cutoff = now - historyDurationMs;
  historyContext.beginPath();
  inputHistory.forEach((sample, index) => {
    const x = Math.max(0, ((sample.time - cutoff) / historyDurationMs) * width);
    const y = 2 + (1 - sample[input]) * (height - 4);
    if (index === 0) historyContext.moveTo(x, y);
    else historyContext.lineTo(x, y);
  });
  historyContext.lineTo(width, 2 + (1 - inputHistory[inputHistory.length - 1][input]) * (height - 4));
  strokeHistoryPath(color);
}

function drawBrakeSignal(now: number, width: number, height: number): void {
  if (!historyContext || inputHistory.length === 0) return;

  const cutoff = now - historyDurationMs;
  const pointFor = (sample: HistorySample) => ({
    x: Math.max(0, ((sample.time - cutoff) / historyDurationMs) * width),
    y: 2 + (1 - sample.brake) * (height - 4)
  });

  let previousPoint = pointFor(inputHistory[0]);
  let segmentColor = brakeColorFor(inputHistory[0].brakeLockup);
  historyContext.beginPath();
  historyContext.moveTo(previousPoint.x, previousPoint.y);

  for (let index = 1; index < inputHistory.length; index += 1) {
    const sample = inputHistory[index];
    const point = pointFor(sample);
    const color = brakeColorFor(sample.brakeLockup);

    if (color !== segmentColor) {
      strokeHistoryPath(segmentColor);
      historyContext.beginPath();
      historyContext.moveTo(previousPoint.x, previousPoint.y);
      segmentColor = color;
    }

    historyContext.lineTo(point.x, point.y);
    previousPoint = point;
  }

  historyContext.lineTo(width, previousPoint.y);
  strokeHistoryPath(segmentColor);
}

function brakeColorFor(lockup: BrakeLockup): string {
  if (lockup !== "none" && lockupColorMode === "single") return singleLockupColor;
  if (lockup === "front") return frontLockupColor;
  if (lockup === "rear") return rearLockupColor;
  if (lockup === "both") return bothLockupColor;
  return BRAKE_COLOR;
}

function applySettings(settings: Awaited<ReturnType<typeof window.overlay.getSnapshot>>["settings"]): void {
  historyDurationMs = settings.graphDurationSeconds * 1000;
  lockupColorMode = settings.lockupColorMode;
  frontLockupColor = settings.lockupColors.front;
  rearLockupColor = settings.lockupColors.rear;
  bothLockupColor = settings.lockupColors.both;
  singleLockupColor = settings.lockupColors.single;
  overlayElement.style.setProperty("--overlay-opacity", String(settings.overlayTransparency));
}

function strokeHistoryPath(color: string): void {
  if (!historyContext) return;

  historyContext.strokeStyle = color;
  historyContext.lineWidth = 2.2;
  historyContext.lineJoin = "round";
  historyContext.lineCap = "round";
  historyContext.shadowColor = color;
  historyContext.shadowBlur = 5;
  historyContext.stroke();
  historyContext.shadowBlur = 0;
}

function setMeter(fill: HTMLDivElement, input: number): void {
  const percentage = Math.round(input * 100);
  fill.style.height = `${Math.min(100, Math.max(0, input * 100))}%`;
  fill.parentElement?.setAttribute("aria-valuenow", String(percentage));
}

function getElement<T extends HTMLElement>(id: string): T {
  const element = document.getElementById(id);
  if (!element) throw new Error(`Missing element #${id}`);
  return element as T;
}

requestAnimationFrame(render);
