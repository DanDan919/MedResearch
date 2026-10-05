import { expect, test, type Page } from "@playwright/test";

const runId = "11111111-1111-4111-8111-111111111111";

test("quantitative workspace presents persisted analysis on desktop and mobile", async ({ page }) => {
  await mockQuantitativeApi(page, [quantitativeArtifact()]);
  await page.goto(`/research/${runId}/quantitative`);

  await expect(page.getByRole("heading", { name: "Scientific synthesis workspace" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Common effect" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "HKSJ interval" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Prediction interval" })).toBeVisible();
  await expect(page.getByRole("img", { name: "Quantitative contribution plot" })).toBeVisible();
  await expect(page.getByText("Study-level confidence intervals are not available in this artifact.")).toBeVisible();

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole("heading", { name: "Scientific synthesis workspace" })).toBeVisible();
  await expect(page.getByRole("table")).toBeVisible();
});

test("quantitative workspace distinguishes an absent artifact", async ({ page }) => {
  await mockQuantitativeApi(page, []);
  await page.goto(`/research/${runId}/quantitative`);

  await expect(page.getByText("No quantitative artifact")).toBeVisible();
  await expect(page.getByText(/does not infer one from the narrative report/)).toBeVisible();
});

async function mockQuantitativeApi(page: Page, artifacts: unknown[]) {
  await page.addInitScript(
    ({ response }) => {
      const originalFetch = window.fetch.bind(window);
      window.fetch = async (input, init) => {
        const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
        if (url.endsWith("/quantitative")) return new Response(JSON.stringify(response), { status: 200, headers: { "Content-Type": "application/json" } });
        if (url.includes("/health/ready")) return new Response("Healthy", { status: 200 });
        return originalFetch(input, init);
      };
    },
    { response: artifacts }
  );
}

function quantitativeArtifact() {
  const contribution = (suffix: string, value: number) => ({
    evidenceId: `00000000-0000-4000-8000-${suffix}`,
    studyId: `00000000-1111-4111-8111-${suffix}`,
    evidenceExtractionId: `00000000-2222-4222-8222-${suffix}`,
    sourceMaterialId: `00000000-3333-4333-8333-${suffix}`,
    analysisScaleEffect: value,
    analysisScaleVariance: 0.1,
    analysisScaleStandardError: 0.316,
    weight: 10,
    normalizedWeight: 0.5
  });
  const contributions = [contribution("000000000001", 0.1), contribution("000000000002", 0.4)];
  return {
    artifactId: "22222222-2222-4222-8222-222222222222",
    persistedAt: "2026-09-29T12:05:00Z",
    snapshotFingerprint: "a".repeat(64),
    result: {
      researchRunId: runId,
      groupKey: "memory|adult|control|trial",
      outcomeGroupKey: "memory",
      populationCompatibilityKey: "adult",
      comparatorCompatibilityKey: "control",
      studyDesignCompatibilityKey: "trial",
      effectMeasureType: 1,
      status: 1,
      method: 0,
      algorithmVersion: "fixed-effect-inverse-variance-v1",
      outputConfidenceLevel: 0.95,
      evidenceCount: 2,
      uniqueStudyCount: 2,
      analysisScaleEffect: 0.25,
      analysisScaleVariance: 0.04,
      analysisScaleStandardError: 0.2,
      analysisScaleConfidenceIntervalLower: -0.14,
      analysisScaleConfidenceIntervalUpper: 0.64,
      reportedScaleEffect: 1.28,
      reportedScaleConfidenceIntervalLower: 0.87,
      reportedScaleConfidenceIntervalUpper: 1.9,
      heterogeneityDiagnostics: { cochransQ: 3.2, degreesOfFreedom: 1, iSquared: 0.6875, studyCount: 2, algorithmVersion: "cochran-q-i2-v1" },
      betweenStudyVariance: { tauSquared: 0, estimator: 0, status: 0, algorithmVersion: "reml-tau-squared-v1", studyCount: 2, converged: true, iterationCount: 4, failureReason: null },
      randomEffects: {
        status: 1,
        method: 1,
        algorithmVersion: "random-effects-inverse-variance-v1",
        confidenceIntervalMethod: 0,
        outputConfidenceLevel: 0.95,
        tauSquared: 0,
        tauSquaredEstimator: 0,
        tauSquaredAlgorithmVersion: "reml-tau-squared-v1",
        studyCount: 2,
        analysisScaleEffect: 0.27,
        analysisScaleVariance: 0.05,
        analysisScaleStandardError: 0.22,
        analysisScaleConfidenceIntervalLower: -0.16,
        analysisScaleConfidenceIntervalUpper: 0.7,
        reportedScaleEffect: 1.31,
        reportedScaleConfidenceIntervalLower: 0.85,
        reportedScaleConfidenceIntervalUpper: 2,
        hksjInference: { status: 1, confidenceIntervalMethod: 1, algorithmVersion: "hksj-v1", outputConfidenceLevel: 0.95, studyCount: 2, degreesOfFreedom: 1, varianceAdjustment: 1.1, criticalValue: 2.2, analysisScaleEffect: 0.27, analysisScaleVariance: 0.055, analysisScaleStandardError: 0.2345, analysisScaleConfidenceIntervalLower: -0.25, analysisScaleConfidenceIntervalUpper: 0.79, reportedScaleEffect: 1.31, reportedScaleConfidenceIntervalLower: 0.78, reportedScaleConfidenceIntervalUpper: 2.2, failureReasons: [] },
        predictionInterval: { status: 1, method: 0, algorithmVersion: "prediction-v1", outputConfidenceLevel: 0.95, studyCount: 2, degreesOfFreedom: 1, tauSquared: 0, summaryEffectVariance: 0.05, summaryEffectStandardError: 0.22, predictionVariance: 0.06, predictionStandardError: 0.245, criticalValue: 2.2, analysisScaleEffect: 0.27, analysisScaleLower: -0.4, analysisScaleUpper: 0.94, reportedScaleEffect: 1.31, reportedScaleLower: 0.67, reportedScaleUpper: 2.56, failureReasons: [] },
        contributions,
        failureReasons: []
      },
      contributions,
      rejectionReasons: []
    }
  };
}
