import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { EvidenceProvenanceWorkspace } from "../components/research/evidence-provenance-workspace";

const runId = "11111111-1111-4111-8111-111111111111";
const now = "2026-10-08T00:00:00Z";
const fetchMock = vi.fn<typeof fetch>();

function attempt(status: string) {
  const success = status.startsWith("Succeeded");
  return { attemptId: runId, researchPlanId: runId, source: "PubMed", query: "query", status, resultCount: success ? status === "SucceededZeroResults" ? 0 : 1 : null, failureCategory: success ? null : status === "TimedOut" ? "Timeout" : status === "Cancelled" ? "Cancelled" : "NetworkFailure", startedAt: now, completedAt: now, literatureSearchId: success ? runId : null };
}
function response(providerAttempts: ReturnType<typeof attempt>[]) {
  return { researchRunId: runId, question: "question", status: "Completed", createdAt: now, startedAt: now, completedAt: now,
    coverage: { researchPlanCount: 1, literatureSearchCount: 1, discoveryPathCount: 0, distinctStudyCount: 0, sourceMaterialCount: 0, evidenceExtractionCount: 0, evidenceFindingCount: 0, evidenceEvaluationCount: 0, researchReportClaimCount: 0, hasPersistedProviderFailureProvenance: providerAttempts.some(a => a.failureCategory !== null) },
    plans: [], searches: [], studies: [], reportClaims: [], quantitativeContributions: [], providerAttempts };
}
function mount() {
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><EvidenceProvenanceWorkspace researchRunId={runId} /></QueryClientProvider>);
}
describe("provider coverage", () => {
  beforeEach(() => { fetchMock.mockReset(); vi.stubGlobal("fetch", fetchMock); });
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
  it.each([["SucceededWithResults", "Succeeded"], ["SucceededZeroResults", "No results"], ["Failed", "Failed"], ["TimedOut", "Timed out"], ["Cancelled", "Cancelled"]])("shows %s as %s", async (status, label) => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(response([attempt(status)]))));
    mount();
    const region = await screen.findByRole("region", { name: "Provider coverage" });
    expect(within(region).getByText(label, { exact: true })).toBeInTheDocument();
    expect(screen.queryByText("Provenance unavailable")).not.toBeInTheDocument();
  });
  it("keeps partial provider failure visible beside success", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(response([attempt("SucceededWithResults"), { ...attempt("Failed"), attemptId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", source: "EuropePmc" }]))));
    mount();
    const region = await screen.findByRole("region", { name: "Provider coverage" });
    expect(within(region).getByText("Succeeded", { exact: true })).toBeInTheDocument();
    expect(within(region).getByText("Failed", { exact: true })).toBeInTheDocument();
  });
  it("does not present an API failure as a provider outcome", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ title: "Unavailable", status: 503 }), { status: 503 }));
    mount();
    expect(await screen.findByText("Provenance unavailable")).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Provider coverage" })).not.toBeInTheDocument();
  });
});
