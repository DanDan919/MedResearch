import { describe, expect, it, vi } from "vitest";
import { MedResearchApiClient } from "../src/client";

describe("MedResearchApiClient research history", () => {
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
