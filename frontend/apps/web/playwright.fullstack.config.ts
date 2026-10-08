import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e-fullstack", testMatch: "*.spec.ts", timeout: 60_000, workers: 1,
  reporter: [["list"], ["json", { outputFile: "test-results/fullstack-results.json" }]],
  use: { baseURL: "https://localhost:3441", ignoreHTTPSErrors: false, trace: "retain-on-failure" },
  projects: [{ name: "actual-api-postgres-trusted-https", use: { ...devices["Desktop Chrome"] } }]
});
