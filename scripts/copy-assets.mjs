import { cp, mkdir } from "node:fs/promises";

const source = new URL("../src/renderer/", import.meta.url);
const destination = new URL("../dist/renderer/", import.meta.url);
const settingsSource = new URL("../src/settings/", import.meta.url);
const settingsDestination = new URL("../dist/settings/", import.meta.url);

await Promise.all([
  mkdir(destination, { recursive: true }),
  mkdir(settingsDestination, { recursive: true })
]);
await Promise.all([
  cp(new URL("index.html", source), new URL("index.html", destination)),
  cp(new URL("styles.css", source), new URL("styles.css", destination)),
  cp(new URL("index.html", settingsSource), new URL("index.html", settingsDestination)),
  cp(new URL("styles.css", settingsSource), new URL("styles.css", settingsDestination))
]);
