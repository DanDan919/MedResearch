import { defineConfig, devices } from "@playwright/test";

if (process.env.MEDRESEARCH_QUICK_ACCEPTANCE !== "true" || process.env.MEDRESEARCH_FULL_STACK !== "true") throw new Error("Actual Q4 API fixture required.");
export default defineConfig({
  testDir: "./e2e-quick", testMatch: "*.spec.ts", timeout: 60000, workers: 1,
  use: { baseURL: "https://localhost:3441", trace: "retain-on-failure",
    // Windows local-only synthetic certificates; Linux CI uses the trusted fixture CA, without a TLS bypass.
    ignoreHTTPSErrors: process.platform === "win32" && Boolean(process.env.MEDRESEARCH_QUICK_LOCAL_CONFIG) },
  projects: [{ name: "actual-quick-api-postgresql", use: { ...devices["Desktop Chrome"] } }]
});
