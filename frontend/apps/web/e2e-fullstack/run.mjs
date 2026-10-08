import { execFileSync, spawn } from "node:child_process";
import { mkdir } from "node:fs/promises";
import { homedir } from "node:os";
import { join, resolve } from "node:path";

if (process.platform !== "linux") throw new Error("The trusted-CA full-stack runner requires Linux and libnss3-tools; run the CI job, not a TLS bypass.");
const originalHome = homedir();
const { ca, temporary, stop, waitUntilReady } = await import("../e2e-auth/server.mjs");
try {
  const nss = join(temporary, ".pki/nssdb"); await mkdir(nss, { recursive: true });
  execFileSync("certutil", ["-N", "-d", `sql:${nss}`, "--empty-password"]);
  execFileSync("certutil", ["-A", "-d", `sql:${nss}`, "-n", "MedResearch isolated test CA", "-t", "C,,", "-i", ca]);
  await waitUntilReady();
  const child = spawn(process.execPath, [resolve("../../node_modules/@playwright/test/cli.js"), "test", "--config", "playwright.fullstack.config.ts"], {
    env: { ...process.env, HOME: temporary, NODE_EXTRA_CA_CERTS: ca,
      PLAYWRIGHT_BROWSERS_PATH: process.env.PLAYWRIGHT_BROWSERS_PATH ?? join(originalHome, ".cache/ms-playwright") },
    stdio: "inherit"
  });
  const code = await new Promise(resolve => child.once("exit", resolve));
  await stop(code ?? 1);
} catch (error) { console.error(error.message); await stop(1); }
