import { describe, expect, it, vi } from "vitest";
import { MedResearchApiClient } from "../src/client";
import { quickResearchDetailsSchema, quickResearchHistorySchema, quickQueryKeys, safeQuickPublicationUrl } from "../src/quick";
import { quickDetails, quickHistory, quickInsufficient, quickProcessing, quickRunId, otherQuickRunId, quickWithContradiction } from "./fixtures/quick";

describe("Quick read contracts", () => {
  it("accepts the API's deterministic non-RFC source Guid without relaxing run/citation membership", () => {
    const details = quickDetails(); const hashed = "abcdef01-2345-f678-0123-456789abcdef";
    details.retrieval!.sources[0].id = hashed;
    details.answer!.findings[0].citations[0].sourceId = hashed;
    details.answer!.sourceReferences = [hashed];
    expect(quickResearchDetailsSchema.safeParse(details).success).toBe(true);
    details.answer!.findings[0].citations[0].sourceId = otherQuickRunId;
    expect(quickResearchDetailsSchema.safeParse(details).success).toBe(false);
  });
  it.each([quickDetails(), quickInsufficient(), quickProcessing(), quickProcessing(true), quickWithContradiction()])("accepts saved state %#", details => {
    expect(quickResearchDetailsSchema.safeParse(details).success).toBe(true);
  });
  it.each(["retrieval", "source", "answer", "root", "reference", "duplicate", "status"])("rejects inconsistent %s", kind => {
    const d = quickDetails();
    if (kind === "retrieval") d.retrieval!.quickRunId = otherQuickRunId;
    if (kind === "source") d.retrieval!.sources[0].quickRunId = otherQuickRunId;
    if (kind === "answer") d.answer!.quickRunId = otherQuickRunId;
    if (kind === "root") d.id = otherQuickRunId;
    if (kind === "reference") d.answer!.findings[0].citations[0].sourceId = otherQuickRunId;
    if (kind === "duplicate") d.answer!.findings[0].citations.push(d.answer!.findings[0].citations[0]);
    if (kind === "status") d.status = "Processing";
    expect(quickResearchDetailsSchema.safeParse(d).success).toBe(false);
  });
  it("isolates session/run/history keys", () => {
    expect(quickQueryKeys.detail("A", quickRunId)).not.toEqual(quickQueryKeys.detail("B", quickRunId));
    expect(quickQueryKeys.detail("A", quickRunId)).not.toEqual(quickQueryKeys.detail("A", otherQuickRunId));
    expect(quickQueryKeys.history("A", 1)).not.toEqual(quickQueryKeys.history("B", 1));
    expect(quickQueryKeys.history("A", 1)).not.toEqual(quickQueryKeys.history("A", 2));
  });
  it("bounds history responses", () => {
    expect(quickResearchHistorySchema.safeParse(quickHistory()).success).toBe(true);
    expect(quickResearchHistorySchema.safeParse({ ...quickHistory(), pageSize: 51 }).success).toBe(false);
  });
  it("GETs only the scoped path with cancellation and no-store", async () => {
    const mock = vi.fn<typeof fetch>().mockImplementation(async () => Response.json(quickDetails()));
    const client = new MedResearchApiClient({ baseUrl: "/api/backend", fetch: mock });
    const signal = new AbortController().signal;
    await client.getQuickResearch(quickRunId, signal);
    expect(mock.mock.calls[0][0]).toBe(`/api/backend/api/quick-research/${quickRunId}`);
    expect(mock.mock.calls[0][1]).toMatchObject({ method: "GET", cache: "no-store", signal });
    mock.mockImplementation(async () => Response.json(quickDetails(otherQuickRunId)));
    await expect(client.getQuickResearch(quickRunId)).rejects.toThrow();
    mock.mockImplementation(async () => Response.json(quickHistory()));
    await client.listQuickResearch(1, signal);
    expect(mock.mock.calls[2][0]).toBe("/api/backend/api/quick-research?page=1&pageSize=20");
    expect(() => client.listQuickResearch(0)).toThrow();
  });
  it("creates with an explicit key, no automatic retry and no owner in the transport", async () => {
    const accepted = { ...quickProcessing(), status: "Queued", startedAt: null };
    const mock = vi.fn<typeof fetch>().mockResolvedValue(Response.json(accepted, { status: 202 }));
    const api = new MedResearchApiClient({ baseUrl: "/api/backend", fetch: mock });
    const signal = new AbortController().signal;
    await api.createQuickResearch("  Как влияет сон на память?  ", quickRunId, signal);
    expect(mock).toHaveBeenCalledTimes(1);
    expect(mock.mock.calls[0][0]).toBe("/api/backend/api/quick-research");
    expect(mock.mock.calls[0][1]).toMatchObject({ method: "POST", cache: "no-store", signal, body: JSON.stringify({ question: "Как влияет сон на память?" }) });
    expect(new Headers(mock.mock.calls[0][1]?.headers).get("idempotency-key")).toBe(quickRunId);
    expect(() => api.createQuickResearch(" ", quickRunId)).toThrow(); expect(() => api.createQuickResearch("x".repeat(2001), quickRunId)).toThrow();
    expect(() => api.createQuickResearch("Question", "bad")).toThrow();
    mock.mockResolvedValue(Response.json({ title: "Unavailable" }, { status: 503 }));
    await expect(api.createQuickResearch("Another question", quickRunId)).rejects.toMatchObject({ status: 503 });
    expect(mock).toHaveBeenCalledTimes(2);
  });
});

describe("Quick publication links", () => {
  it("still rejects an empty Guid rather than weakening persisted identity validation", () => {
    const details = quickDetails();
    details.id = "00000000-0000-0000-0000-000000000000";
    expect(quickResearchDetailsSchema.safeParse(details).success).toBe(false);
  });
  it.each(["javascript:alert(1)", "data:text/html,evil", "http://pubmed.ncbi.nlm.nih.gov/12345678/", "//pubmed.ncbi.nlm.nih.gov/12345678/",
    "https://evil.example/12345678/", "https://pubmed.ncbi.nlm.nih.gov.evil.example/12345678/", "https://user:pass@pubmed.ncbi.nlm.nih.gov/12345678/",
    "https://pubmed.ncbi.nlm.nih.gov:8443/12345678/", "https://pubmed.ncbi.nlm.nih.gov/99999999/", "https://pubmed.ncbi.nlm.nih.gov/12345678/?redirect=evil",
    "https://pubmed.ncbi.nlm.nih.gov/12345678/#evil", "https://doi.org/%zz", "https://doi.org/10.1234/wrong"])("rejects %s", url => {
    expect(safeQuickPublicationUrl({ ...quickDetails().retrieval!.sources[0], url })).toBeNull();
  });
  it("accepts only supplied identifier-matching HTTPS links; missing remains missing", () => {
    const s = quickDetails().retrieval!.sources[0];
    expect(safeQuickPublicationUrl(s)).toBe(s.url);
    expect(safeQuickPublicationUrl({ ...s, url: "https://europepmc.org/articles/PMC1234567" })).toBe("https://europepmc.org/articles/PMC1234567");
    expect(safeQuickPublicationUrl({ ...s, url: "https://doi.org/10.1234%2Fsynthetic" })).toBe("https://doi.org/10.1234%2Fsynthetic");
    expect(safeQuickPublicationUrl({ ...s, url: null })).toBeNull();
  });
});
