import { cp, mkdir } from "node:fs/promises";

const source = new URL("../src/renderer/", import.meta.url);
const destination = new URL("../dist/renderer/", import.meta.url);

await mkdir(destination, { recursive: true });
await Promise.all([
  cp(new URL("index.html", source), new URL("index.html", destination)),
  cp(new URL("styles.css", source), new URL("styles.css", destination))
]);
