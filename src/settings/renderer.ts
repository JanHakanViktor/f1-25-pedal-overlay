type LockupColorMode = "axle" | "single";

interface FormSettings {
  steeringEnabledByDefault: boolean;
  overlayTransparency: number;
  udpPort: number;
  lockupSensitivity: number;
  graphDurationSeconds: number;
  shortcuts: {
    toggleVisibility: string;
    toggleLock: string;
    toggleDemo: string;
    toggleSteering: string;
    quit: string;
  };
  lockupColorMode: LockupColorMode;
  lockupColors: {
    front: string;
    rear: string;
    both: string;
    single: string;
  };
}

const form = getSettingsElement<HTMLFormElement>("settingsForm");
const saveStatus = getSettingsElement<HTMLOutputElement>("saveStatus");
const transparencyInput = getSettingsElement<HTMLInputElement>("overlayTransparency");
const transparencyValue = getSettingsElement<HTMLOutputElement>("overlayTransparencyValue");
const sensitivityInput = getSettingsElement<HTMLInputElement>("lockupSensitivity");
const sensitivityValue = getSettingsElement<HTMLOutputElement>("lockupSensitivityValue");
const axleColours = getSettingsElement<HTMLDivElement>("axleColours");
const singleColour = getSettingsElement<HTMLDivElement>("singleColour");

transparencyInput.addEventListener("input", updateRangeLabels);
sensitivityInput.addEventListener("input", updateRangeLabels);
document.querySelectorAll<HTMLInputElement>('input[name="lockupColorMode"]').forEach((input) => {
  input.addEventListener("change", updateColourMode);
});
getSettingsElement<HTMLButtonElement>("closeButton").addEventListener("click", () => window.settings.close());

form.addEventListener("submit", (event) => {
  event.preventDefault();
  void saveSettings();
});

void loadSettings();

async function loadSettings(): Promise<void> {
  const settings = await window.settings.getSettings();
  setChecked("steeringEnabledByDefault", settings.steeringEnabledByDefault);
  setValue("overlayTransparency", String(Math.round(settings.overlayTransparency * 100)));
  setValue("udpPort", String(settings.udpPort));
  setValue("lockupSensitivity", String(Math.round(settings.lockupSensitivity * 100)));
  setValue("graphDurationSeconds", String(settings.graphDurationSeconds));
  setValue("toggleVisibilityShortcut", settings.shortcuts.toggleVisibility);
  setValue("toggleLockShortcut", settings.shortcuts.toggleLock);
  setValue("toggleDemoShortcut", settings.shortcuts.toggleDemo);
  setValue("toggleSteeringShortcut", settings.shortcuts.toggleSteering);
  setValue("quitShortcut", settings.shortcuts.quit);
  setValue("frontLockupColor", settings.lockupColors.front);
  setValue("rearLockupColor", settings.lockupColors.rear);
  setValue("bothLockupColor", settings.lockupColors.both);
  setValue("singleLockupColor", settings.lockupColors.single);

  const mode = document.querySelector<HTMLInputElement>(
    `input[name="lockupColorMode"][value="${settings.lockupColorMode}"]`
  );
  if (mode) mode.checked = true;
  updateRangeLabels();
  updateColourMode();
}

async function saveSettings(): Promise<void> {
  saveStatus.classList.remove("is-error");
  saveStatus.textContent = "Saving…";

  const result = await window.settings.saveSettings(readForm());
  if (!result.ok) {
    saveStatus.classList.add("is-error");
    saveStatus.textContent = result.error ?? "Settings could not be saved.";
    return;
  }

  saveStatus.textContent = "Saved. Steering default applies on the next launch.";
}

function readForm(): FormSettings {
  const selectedMode = document.querySelector<HTMLInputElement>('input[name="lockupColorMode"]:checked');
  return {
    steeringEnabledByDefault: getSettingsElement<HTMLInputElement>("steeringEnabledByDefault").checked,
    overlayTransparency: Number.parseInt(transparencyInput.value, 10) / 100,
    udpPort: Number.parseInt(getValue("udpPort"), 10),
    lockupSensitivity: Number.parseInt(sensitivityInput.value, 10) / 100,
    graphDurationSeconds: Number.parseFloat(getValue("graphDurationSeconds")),
    shortcuts: {
      toggleVisibility: getValue("toggleVisibilityShortcut"),
      toggleLock: getValue("toggleLockShortcut"),
      toggleDemo: getValue("toggleDemoShortcut"),
      toggleSteering: getValue("toggleSteeringShortcut"),
      quit: getValue("quitShortcut")
    },
    lockupColorMode: selectedMode?.value === "single" ? "single" : "axle",
    lockupColors: {
      front: getValue("frontLockupColor"),
      rear: getValue("rearLockupColor"),
      both: getValue("bothLockupColor"),
      single: getValue("singleLockupColor")
    }
  };
}

function updateRangeLabels(): void {
  transparencyValue.textContent = `${transparencyInput.value}%`;
  sensitivityValue.textContent = `${sensitivityInput.value}%`;
}

function updateColourMode(): void {
  const selectedMode = document.querySelector<HTMLInputElement>('input[name="lockupColorMode"]:checked');
  const useSingleColour = selectedMode?.value === "single";
  axleColours.classList.toggle("is-hidden", useSingleColour);
  singleColour.classList.toggle("is-hidden", !useSingleColour);
}

function setValue(id: string, value: string): void {
  getSettingsElement<HTMLInputElement>(id).value = value;
}

function getValue(id: string): string {
  return getSettingsElement<HTMLInputElement>(id).value.trim();
}

function setChecked(id: string, value: boolean): void {
  getSettingsElement<HTMLInputElement>(id).checked = value;
}

function getSettingsElement<T extends HTMLElement>(id: string): T {
  const element = document.getElementById(id);
  if (!element) throw new Error(`Missing element #${id}`);
  return element as T;
}
