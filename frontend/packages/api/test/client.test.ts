import { describe, expect, it, vi } from "vitest";
import { MedResearchApiClient } from "../src/client";

describe("MedResearchApiClient research history", () => {
  it("adds a bearer token through the centralized transport option", async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }));
    const client = new MedResearchApiClient({
      baseUrl: "https://api.example.test",
      fetch: fetchMock,
      getAccessToken: () => "test-token"
    });

    await client.listResearchRuns();

    const init = fetchMock.mock.calls[0][1];
    expect(new Headers(init?.headers).get("Authorization")).toBe("Bearer test-token");
  });

  it("requests paged research runs with default filters", async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      jsonResponse({
        items: [],
        page: 1,
        pageSize: 20,
        totalCount: 0,
        totalPages: 0
      })
    );
    const client = new MedResearchApiClient({ baseUrl: "https://api.example.test/", fetch: fetchMock });

    const result = await client.listResearchRuns();

    expect(result.totalCount).toBe(0);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.example.test/api/research?page=1&pageSize=20");
    expect(init?.method).toBe("GET");
  });

  it("encodes explicit pagination and status filters", async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      jsonResponse({
        items: [
          {
            researchRunId: "11111111-1111-4111-8111-111111111111",
            researchQuestionId: "22222222-2222-4222-8222-222222222222",
            question: "Does sleep deprivation impair memory?",
            status: "Completed",
            createdAt: "2026-09-28T12:00:00Z",
            startedAt: "2026-09-28T12:01:00Z",
            completedAt: "2026-09-28T12:05:00Z",
            failureReason: null
          }
        ],
        page: 2,
        pageSize: 10,
        totalCount: 11,
        totalPages: 2
      })
    );
    const client = new MedResearchApiClient({ baseUrl: "https://api.example.test", fetch: fetchMock });

    const result = await client.listResearchRuns({ page: 2, pageSize: 10, status: "Completed" });

    expect(result.items[0].status).toBe("Completed");
    const [url] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.example.test/api/research?page=2&pageSize=10&status=Completed");
  });
});

describe("MedResearchApiClient authentication errors", () => {
  it("classifies an unauthorized API response distinctly from a missing resource", async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(JSON.stringify({ title: "Unauthorized", status: 401 }), {
        status: 401,
        headers: { "Content-Type": "application/problem+json" }
      })
    );
    const client = new MedResearchApiClient({ baseUrl: "https://api.example.test", fetch: fetchMock });

    await expect(client.listResearchRuns()).rejects.toMatchObject({ kind: "unauthorized", status: 401 });
  });
});

describe("MedResearchApiClient research progress", () => {
  it("requests persisted progress for a research run", async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse(progressResponse()));
    const client = new MedResearchApiClient({ baseUrl: "https://api.example.test", fetch: fetchMock });

    const result = await client.getResearchProgress("11111111-1111-4111-8111-111111111111");

    expect(result.metrics.distinctDiscoveredStudyCount).toBe(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe(
      "https://api.example.test/api/research/11111111-1111-4111-8111-111111111111/progress"
    );
    expect(init?.method).toBe("GET");
  });
});

describe("MedResearchApiClient quantitative results", () => {
  it("reads the persisted artifact endpoint with the run-scoped path", async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse([quantitativeArtifactResponse()]));
    const client = new MedResearchApiClient({ baseUrl: "https://api.example.test", fetch: fetchMock });

    const result = await client.getQuantitativeSynthesisArtifacts("11111111-1111-4111-8111-111111111111");

    expect(result).toHaveLength(1);
    expect(result[0].result.randomEffects?.hksjInference?.confidenceIntervalMethod).toBe(1);
    expect(String(fetchMock.mock.calls[0][0])).toBe("https://api.example.test/api/research/11111111-1111-4111-8111-111111111111/quantitative");
  });
});

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
}

function progressResponse() {
  return {
    researchRunId: "11111111-1111-4111-8111-111111111111",
    question: "Does sleep improve recall?",
    status: "Searching",
    createdAt: "2026-09-28T12:00:00Z",
    startedAt: "2026-09-28T12:01:00Z",
    completedAt: null,
    failureReason: null,
    refreshedAt: "2026-09-28T12:02:00Z",
    processing: {
      leaseState: "Active",
      leaseExpiresAt: "2026-09-28T12:05:00Z",
      lastHeartbeatAt: "2026-09-28T12:02:00Z",
      leaseVersion: 1
    },
    metrics: {
      researchPlanCount: 1,
      plannedSearchQueryCount: 1,
      literatureSearchCount: 1,
      literatureSearchSourceCount: 1,
      literatureSearchResultCount: 3,
      discoveryPathCount: 1,
      distinctDiscoveredStudyCount: 1,
      currentSourceMaterialCount: 1,
      structuredFullTextMaterialCount: 0,
      abstractMaterialCount: 1,
      evidenceExtractionCount: 0,
      completedEvidenceExtractionCount: 0,
      skippedEvidenceExtractionCount: 0,
      evidenceFindingCount: 0,
      evidenceEvaluationCount: 0,
      completedEvidenceEvaluationCount: 0,
      skippedEvidenceEvaluationCount: 0,
      researchReportCount: 0,
      researchReportClaimCount: 0
    },
    stages: [
      { stage: "Queued", state: "Completed", metrics: [{ label: "Run records", value: 1 }] },
      { stage: "Planning", state: "Completed", metrics: [{ label: "Plans", value: 1 }] },
      { stage: "Searching", state: "Current", metrics: [{ label: "Searches", value: 1 }] },
      { stage: "Extracting", state: "Pending", metrics: [{ label: "Extractions", value: 0 }] },
      { stage: "Evaluating", state: "Pending", metrics: [{ label: "Evaluations", value: 0 }] },
      { stage: "Synthesizing", state: "Pending", metrics: [{ label: "Reports", value: 0 }] },
      { stage: "Completed", state: "Pending", metrics: [{ label: "Reports", value: 0 }] }
    ]
  };
}

function quantitativeArtifactResponse() {
  const contribution = {
    evidenceId: "44444444-4444-4444-8444-444444444444",
    studyId: "55555555-5555-4555-8555-555555555555",
    evidenceExtractionId: "66666666-6666-4666-8666-666666666666",
    sourceMaterialId: "77777777-7777-4777-8777-777777777777",
    analysisScaleEffect: 0.2,
    analysisScaleVariance: 0.1,
    analysisScaleStandardError: 0.316,
    weight: 10,
    normalizedWeight: 1
  };
  return {
    artifactId: "22222222-2222-4222-8222-222222222222",
    persistedAt: "2026-09-29T12:05:00Z",
    snapshotFingerprint: "a".repeat(64),
    result: {
      researchRunId: "11111111-1111-4111-8111-111111111111",
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
      evidenceCount: 1,
      uniqueStudyCount: 1,
      analysisScaleEffect: 0.2,
      analysisScaleVariance: 0.1,
      analysisScaleStandardError: 0.316,
      analysisScaleConfidenceIntervalLower: -0.4,
      analysisScaleConfidenceIntervalUpper: 0.8,
      reportedScaleEffect: 1.22,
      reportedScaleConfidenceIntervalLower: 0.67,
      reportedScaleConfidenceIntervalUpper: 2.22,
      heterogeneityDiagnostics: null,
      betweenStudyVariance: null,
      randomEffects: {
        status: 1,
        method: 1,
        algorithmVersion: "random-effects-inverse-variance-v1",
        confidenceIntervalMethod: 0,
        outputConfidenceLevel: 0.95,
        tauSquared: 0,
        tauSquaredEstimator: 0,
        tauSquaredAlgorithmVersion: "reml-tau-squared-v1",
        studyCount: 1,
        analysisScaleEffect: 0.2,
        analysisScaleVariance: 0.1,
        analysisScaleStandardError: 0.316,
        analysisScaleConfidenceIntervalLower: -0.4,
        analysisScaleConfidenceIntervalUpper: 0.8,
        reportedScaleEffect: 1.22,
        reportedScaleConfidenceIntervalLower: 0.67,
        reportedScaleConfidenceIntervalUpper: 2.22,
        hksjInference: {
          status: 0,
          confidenceIntervalMethod: 1,
          algorithmVersion: "hksj-v1",
          outputConfidenceLevel: 0.95,
          studyCount: 1,
          degreesOfFreedom: null,
          varianceAdjustment: null,
          criticalValue: null,
          analysisScaleEffect: null,
          analysisScaleVariance: null,
          analysisScaleStandardError: null,
          analysisScaleConfidenceIntervalLower: null,
          analysisScaleConfidenceIntervalUpper: null,
          reportedScaleEffect: null,
          reportedScaleConfidenceIntervalLower: null,
          reportedScaleConfidenceIntervalUpper: null,
          failureReasons: [1]
        },
        predictionInterval: null,
        contributions: [contribution],
        failureReasons: []
      },
      contributions: [contribution],
      rejectionReasons: []
    }
  };
}
