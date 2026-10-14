import { execFileSync, spawn } from "node:child_process";
import { mkdir } from "node:fs/promises";
import { homedir } from "node:os";
import { join, resolve } from "node:path";
import process from "node:process";
import { error as logError } from "node:console";

if (process.env.MEDRESEARCH_QUICK_ACCEPTANCE !== "true" || process.env.MEDRESEARCH_FULL_STACK !== "true") throw new Error("Explicit actual API/Q4 fixture required.");
if (process.platform !== "linux" && !(process.platform === "win32" && process.env.MEDRESEARCH_QUICK_LOCAL_CONFIG)) throw new Error("Unsupported Q4 verification platform.");
const originalHome = homedir();
const { ca, temporary, stop, waitUntilReady } = await import("../e2e-auth/server.mjs");
try {
  const browserEnv = { ...process.env, NODE_EXTRA_CA_CERTS: ca };
  if (process.platform === "linux") {
    const dataHome = join(temporary, ".local/share");
    const nss = join(dataHome, "pki/nssdb"); await mkdir(nss, { recursive: true });
    execFileSync("certutil", ["-N", "-d", `sql:${nss}`, "--empty-password"]);
    execFileSync("certutil", ["-A", "-d", `sql:${nss}`, "-n", "MedResearch isolated Q4 test CA", "-t", "C,,", "-i", ca]);
    Object.assign(browserEnv, { HOME: temporary, XDG_DATA_HOME: dataHome,
      PLAYWRIGHT_BROWSERS_PATH: process.env.PLAYWRIGHT_BROWSERS_PATH ?? join(originalHome, ".cache/ms-playwright") });
  }
  await waitUntilReady();
  const child = spawn(process.execPath, [resolve("../../node_modules/@playwright/test/cli.js"), "test", "--config", "playwright.quick.config.ts"], {
    env: browserEnv, windowsHide: true, stdio: "inherit"
  });
  const code = await new Promise(resolve => child.once("exit", resolve));
  await stop(code ?? 1);
} catch (error) { logError(error.message); await stop(1); }
