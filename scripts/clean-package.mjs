import { rm } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDirectory = path.join(projectRoot, "out");

if (path.dirname(outputDirectory) !== projectRoot || path.basename(outputDirectory) !== "out") {
  throw new Error("Refusing to clean an unexpected packaging directory.");
}

await rm(outputDirectory, { recursive: true, force: true });
