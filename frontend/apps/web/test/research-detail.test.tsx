import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ResearchDetail } from "../components/research/research-detail";

const fetchMock = vi.fn<typeof fetch>();

describe("ResearchDetail", () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
  });

  it("renders live progress from the backend progress endpoint", async () => {
    fetchMock.mockResolvedValue(jsonResponse(progressResponse({ status: "Searching" })));

    renderWithClient(<ResearchDetail researchRunId="11111111-1111-4111-8111-111111111111" />);

    expect(await screen.findByText("Does sleep improve recall?")).toBeInTheDocument();
    expect(screen.getAllByText("Searching").length).toBeGreaterThan(0);
    expect(screen.getByText("Distinct studies")).toBeInTheDocument();
    expect(screen.getAllByText("1").length).toBeGreaterThan(0);
    expect(screen.getByText("Active until 2026-09-28T12:05:00Z")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /open report/i })).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      "http://localhost:8080/api/research/11111111-1111-4111-8111-111111111111/progress",
      expect.objectContaining({ method: "GET" })
    );
  });

  it("shows completed report navigation when the run is complete", async () => {
    fetchMock.mockResolvedValue(jsonResponse(progressResponse({ status: "Completed" })));

    renderWithClient(<ResearchDetail researchRunId="11111111-1111-4111-8111-111111111111" />);

    expect(await screen.findByRole("link", { name: /open report/i })).toHaveAttribute(
      "href",
      "/research/11111111-1111-4111-8111-111111111111/report"
    );
  });

  it("renders safe failure state without inventing a failed pipeline stage", async () => {
    fetchMock.mockResolvedValue(
      jsonResponse(progressResponse({ status: "Failed", failureReason: "Research processing failed." }))
    );

    renderWithClient(<ResearchDetail researchRunId="11111111-1111-4111-8111-111111111111" />);

    expect(await screen.findByText("Processing Failed")).toBeInTheDocument();
    expect(screen.getByText("Research processing failed.")).toBeInTheDocument();
  });
});

function renderWithClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
}

function progressResponse({
  status,
  failureReason = null
}: {
  status: "Searching" | "Completed" | "Failed";
  failureReason?: string | null;
}) {
  const terminal = status === "Completed" || status === "Failed";

  return {
    researchRunId: "11111111-1111-4111-8111-111111111111",
    question: "Does sleep improve recall?",
    status,
    createdAt: "2026-09-28T12:00:00Z",
    startedAt: "2026-09-28T12:01:00Z",
    completedAt: terminal ? "2026-09-28T12:05:00Z" : null,
    failureReason,
    refreshedAt: "2026-09-28T12:02:00Z",
    processing: {
      leaseState: terminal ? "Terminal" : "Active",
      leaseExpiresAt: terminal ? null : "2026-09-28T12:05:00Z",
      lastHeartbeatAt: terminal ? null : "2026-09-28T12:02:00Z",
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
      evidenceExtractionCount: status === "Searching" ? 0 : 1,
      completedEvidenceExtractionCount: status === "Searching" ? 0 : 1,
      skippedEvidenceExtractionCount: 0,
      evidenceFindingCount: status === "Searching" ? 0 : 1,
      evidenceEvaluationCount: status === "Completed" ? 1 : 0,
      completedEvidenceEvaluationCount: status === "Completed" ? 1 : 0,
      skippedEvidenceEvaluationCount: 0,
      researchReportCount: status === "Completed" ? 1 : 0,
      researchReportClaimCount: status === "Completed" ? 1 : 0
    },
    stages: [
      { stage: "Queued", state: "Completed", metrics: [{ label: "Run records", value: 1 }] },
      { stage: "Planning", state: "Completed", metrics: [{ label: "Plans", value: 1 }] },
      { stage: "Searching", state: status === "Searching" ? "Current" : "Completed", metrics: [{ label: "Searches", value: 1 }] },
      { stage: "Extracting", state: status === "Completed" ? "Completed" : "Pending", metrics: [{ label: "Extractions", value: status === "Searching" ? 0 : 1 }] },
      { stage: "Evaluating", state: status === "Completed" ? "Completed" : "Pending", metrics: [{ label: "Evaluations", value: status === "Completed" ? 1 : 0 }] },
      { stage: "Synthesizing", state: status === "Completed" ? "Completed" : "Pending", metrics: [{ label: "Reports", value: status === "Completed" ? 1 : 0 }] },
      { stage: "Completed", state: status === "Completed" ? "Completed" : "Pending", metrics: [{ label: "Reports", value: status === "Completed" ? 1 : 0 }] }
    ]
  };
}
