const throttleFill = getElement<HTMLDivElement>("throttleFill");
const brakeFill = getElement<HTMLDivElement>("brakeFill");
const overlayElement = getElement<HTMLElement>("overlay");
const steeringGauge = getElement<HTMLElement>("steeringGauge");
const steeringDial = getElement<HTMLElement>("steeringDial");
const steeringMarker = getElement<HTMLDivElement>("steeringMarker");
const steeringValue = getElement<HTMLOutputElement>("steeringValue");
const historyCanvas = getElement<HTMLCanvasElement>("historyCanvas");
const historyContext = historyCanvas.getContext("2d");

interface HistorySample {
  time: number;
  throttle: number;
  brake: number;
}

const HISTORY_DURATION_MS = 5000;
const SAMPLE_INTERVAL_MS = 40;
const MAX_STEERING_DEGREES = 180;
const STEERING_MARKER_TRAVEL_PX = 24;
const inputHistory: HistorySample[] = [];

let shownThrottle = 0;
let shownBrake = 0;
let shownSteering = 0;
let targetThrottle = 0;
let targetBrake = 0;
let targetSteering = 0;
let lastSampleAt = -SAMPLE_INTERVAL_MS;
let snapshotPending = false;

window.overlay.onTelemetry((telemetry) => {
  targetThrottle = telemetry.throttle;
  targetBrake = telemetry.brake;
  targetSteering = telemetry.steering;
});

window.overlay.onLockChanged((locked) => {
  overlayElement.classList.toggle("is-locked", locked);
});

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
    overlayElement.classList.toggle("is-locked", snapshot.locked);
    overlayElement.classList.toggle("has-steering", snapshot.steeringEnabled);
    steeringGauge.hidden = !snapshot.steeringEnabled;
  } catch {
    // The main process may be closing while a scheduled refresh is in flight.
  } finally {
    snapshotPending = false;
  }
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
    inputHistory.push({ time: now, throttle: shownThrottle, brake: shownBrake });
    lastSampleAt = now;
  }

  const cutoff = now - HISTORY_DURATION_MS;
  while (inputHistory.length > 1 && inputHistory[1].time < cutoff) inputHistory.shift();
  drawHistory(now);
  requestAnimationFrame(render);
}

function setSteering(input: number): void {
  const normalized = Math.min(1, Math.max(-1, input));
  const degrees = Math.round(normalized * MAX_STEERING_DEGREES);
  const markerOffset = normalized * STEERING_MARKER_TRAVEL_PX;

  steeringMarker.style.setProperty("--steer-offset", `${markerOffset}px`);
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
  drawSignal("throttle", "#42e37c", now, width, height);
  drawSignal("brake", "#ff4261", now, width, height);
}

function drawSignal(
  input: "throttle" | "brake",
  color: string,
  now: number,
  width: number,
  height: number
): void {
  if (!historyContext || inputHistory.length === 0) return;

  const cutoff = now - HISTORY_DURATION_MS;
  historyContext.beginPath();
  inputHistory.forEach((sample, index) => {
    const x = Math.max(0, ((sample.time - cutoff) / HISTORY_DURATION_MS) * width);
    const y = 2 + (1 - sample[input]) * (height - 4);
    if (index === 0) historyContext.moveTo(x, y);
    else historyContext.lineTo(x, y);
  });
  historyContext.lineTo(width, 2 + (1 - inputHistory[inputHistory.length - 1][input]) * (height - 4));
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
