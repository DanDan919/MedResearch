import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e-auth", testMatch: "*.spec.ts", timeout: 60_000, workers: 1,
  use: { baseURL: "https://localhost:3441", ignoreHTTPSErrors: true, trace: "retain-on-failure" },
  webServer: { command: "node e2e-auth/server.mjs", url: "https://localhost:3441/_fixture/ready", ignoreHTTPSErrors: true,
    reuseExistingServer: false, timeout: 120_000 },
  projects: [{ name: "production-oidc-chromium", use: { ...devices["Desktop Chrome"] } }]
});
