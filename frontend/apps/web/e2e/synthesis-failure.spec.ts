import { expect, test } from "@playwright/test";

const runId = "11111111-1111-4111-8111-111111111111";
const reason = "The report could not be generated because the response exceeded the allowed claim limit. Previously processed sources remain available.";
const progress = {
  researchRunId: runId, question: "Synthetic source-scoped question", status: "Failed",
  createdAt: "2026-10-11T09:00:00Z", startedAt: "2026-10-11T09:01:00Z", completedAt: "2026-10-11T09:31:00Z",
  failureReason: reason, failureCategory: "ReportClaimLimitExceeded", refreshedAt: "2026-10-11T09:32:00Z",
  processing: { leaseState: "Terminal", leaseExpiresAt: null, lastHeartbeatAt: null, leaseVersion: 1 },
  metrics: {
    researchPlanCount: 1, plannedSearchQueryCount: 1, literatureSearchCount: 3, literatureSearchSourceCount: 3,
    literatureSearchResultCount: 60, discoveryPathCount: 60, distinctDiscoveredStudyCount: 60,
    currentSourceMaterialCount: 10, structuredFullTextMaterialCount: 0, abstractMaterialCount: 10,
    evidenceExtractionCount: 10, completedEvidenceExtractionCount: 10, skippedEvidenceExtractionCount: 0,
    evidenceFindingCount: 29, evidenceEvaluationCount: 10, completedEvidenceEvaluationCount: 10,
    skippedEvidenceEvaluationCount: 0, researchReportCount: 0, researchReportClaimCount: 0
  },
  stages: [
    { stage: "Queued", state: "Completed", metrics: [{ label: "Run records", value: 1 }] },
    { stage: "Planning", state: "Completed", metrics: [{ label: "Plans", value: 1 }] },
    { stage: "Searching", state: "Completed", metrics: [{ label: "Searches", value: 3 }] },
    { stage: "Extracting", state: "Completed", metrics: [{ label: "Evidence", value: 29 }] },
    { stage: "Evaluating", state: "Completed", metrics: [{ label: "Evaluations", value: 10 }] },
    { stage: "Synthesizing", state: "Pending", metrics: [{ label: "Reports", value: 0 }] },
    { stage: "Completed", state: "Pending", metrics: [{ label: "Reports", value: 0 }] }
  ]
};

for (const width of [1280, 390]) {
  test(`bounded report recovery preserves an ambiguous submission key at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    const keys: string[] = [];
    let state = { researchRunId: runId, availability: "Available", retryAttemptId: null as string | null, attemptStatus: null as string | null, generationBudget: 2, generationCallsReserved: 0, failureCategory: null };
    await page.route("**/api/backend/**", async route => {
      const url = new URL(route.request().url());
      if (url.pathname.endsWith("/progress")) return route.fulfill({ json: progress });
      if (url.pathname.endsWith("/health/ready")) return route.fulfill({ body: "Healthy" });
      if (url.pathname.endsWith("/retry-synthesis")) {
        if (route.request().method() === "POST") {
          keys.push(route.request().headers()["idempotency-key"]);
          expect(route.request().postDataJSON()).toEqual({});
          if (keys.length === 1) return route.fulfill({ status: 503, json: { title: "Ambiguous test response" } });
          state = { ...state, availability: "Active", retryAttemptId: runId, attemptStatus: "Queued" };
          return route.fulfill({ status: 202, json: state });
        }
        return route.fulfill({ json: state });
      }
      throw new Error(`Unexpected test request: ${url.pathname}`);
    });
    await page.goto(`/research/${runId}`);
    const action = page.getByRole("button", { name: "Retry report generation" });
    await expect(action).toBeVisible();
    await action.click();
    await expect(page.getByRole("region", { name: "Report recovery" }).getByRole("alert")).toBeVisible();
    await action.click();
    await expect(page.getByText("Report retry queued", { exact: true })).toBeVisible();
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
    await expect(action).toHaveCount(0);
    await page.reload();
    await expect(page.getByText("Report retry queued", { exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: /Evidence.*Provenance/ })).toBeVisible();
    expect(keys).toHaveLength(2);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    expect(errors).toEqual([]);
    await page.screenshot({ path: testInfo.outputPath(`recovery-${width}.png`), fullPage: true });
  });

  test(`failed synthesis retains partial navigation without hydration errors at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });
    await page.route("**/api/backend/**", async route => {
      const url = new URL(route.request().url());
      if (url.pathname.endsWith("/progress")) return route.fulfill({ json: progress });
      if (url.pathname.endsWith("/retry-synthesis")) return route.fulfill({ json: { researchRunId: runId, availability: "Unavailable", retryAttemptId: null, attemptStatus: null, generationBudget: 2, generationCallsReserved: 0, failureCategory: null } });
      if (url.pathname.endsWith("/report")) return route.fulfill({ status: 409, json: { title: "Report not ready", status: 409 } });
      if (url.pathname.endsWith("/health/ready")) return route.fulfill({ body: "Healthy" });
      throw new Error(`Unexpected test request: ${url.pathname}`);
    });
    await page.goto(`/research/${runId}`);
    await expect(page.getByText("Report unavailable", { exact: true })).toBeVisible();
    await expect(page.getByRole("list", { name: "Research phases" }).locator("li")).toHaveCount(3);
    await expect(page.getByText("Previously processed evidence is still available. No report was generated.")).toBeVisible();
    await expect(page.getByRole("link", { name: /Evidence.*Provenance/ })).toHaveAttribute("href", `/research/${runId}/evidence`);
    await expect(page.getByRole("link", { name: "Open report" })).toHaveCount(0);
    await expect(page.getByText("Technical stages").locator("..")).not.toHaveAttribute("open");
    await page.getByText("Technical stages").click();
    await expect(page.getByText("Synthesizing", { exact: true })).toBeVisible();
    await expect(page.getByText("Reports", { exact: true }).first()).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`phases-${width}.png`), fullPage: true });
    await page.goto(`/research/${runId}/report`);
    await expect(page.getByText(reason, { exact: true })).toBeVisible();
    await expect(page.getByText(/Check back after synthesis/)).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Evidence and source provenance" })).toHaveAttribute("href", `/research/${runId}/evidence`);
    expect(errors.filter(error => !error.includes("409 (Conflict)"))).toEqual([]);
  });
}
