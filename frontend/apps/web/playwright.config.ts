import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  timeout: 30_000,
  use: {
    baseURL: "http://127.0.0.1:3017",
    trace: "on-first-retry"
  },
  webServer: {
    command: "pnpm exec next dev --hostname 127.0.0.1 --port 3017",
    env: { WEB_AUTH_MODE: "DevelopmentLocal", WEB_AUTH_ORIGIN: "http://127.0.0.1:3017" },
    url: "http://127.0.0.1:3017",
    reuseExistingServer: false,
    timeout: 120_000
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] }
    }
  ]
});
