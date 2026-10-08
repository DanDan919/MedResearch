import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ResearchReport } from "../components/research/research-report";

const fetchMock = vi.fn<typeof fetch>();

describe("ResearchReport", () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
  });

  it("renders the report narrative and traceable evidence workspace", async () => {
    fetchMock.mockResolvedValue(jsonResponse(reportResponse()));

    renderWithClient(<ResearchReport researchRunId={runId} />);

    expect(await screen.findByRole("heading", { name: "Research Report" })).toBeInTheDocument();
    expect(screen.getByText("Executive summary from persisted report.")).toBeInTheDocument();
    expect(screen.getByText("Authoritative study title")).toBeInTheDocument();
    expect(screen.queryByText("92%")).not.toBeInTheDocument();
    expect(screen.getByText("Legacy narrative unverified")).toBeInTheDocument();
    expect(screen.getByText("Legacy claim unverified")).toBeInTheDocument();

    fireEvent.click(screen.getByText("Authoritative study title"));

    expect(screen.getByText("Extracted supporting text")).toBeInTheDocument();
    expect(screen.getByText("Recall improved after sleep.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /PMID: 12345678/i })).toHaveAttribute(
      "href",
      "https://pubmed.ncbi.nlm.nih.gov/12345678/"
    );
    expect(screen.getByText(/Abstract from PubMed via SearchMetadataAbstract/)).toBeInTheDocument();
  });

  it.each(["QualitativeEffect", "MixedEvidence", "InsufficientEvidence", "QuantitativeSynthesis"])("preserves %s backend claim and exposes declared support", async (kind) => {
    const report = reportResponse();
    const source = report.claims[0];
    const numeric = kind === "QuantitativeSynthesis";
    const insufficient = kind === "InsufficientEvidence";
    const direction = numeric || insufficient ? "NotApplicable" : kind === "MixedEvidence" ? "Mixed" : "Positive";
    const text = kind === "MixedEvidence" ? "The cited Evidence has mixed directions, not consistent benefit."
      : insufficient ? "Insufficient Evidence is not evidence of no effect."
      : numeric ? "Random-effects HKSJ OddsRatio = 0.73; 95% confidence interval [0.40, 1.20]."
      : "Within the cited Evidence, findings have a positive reported direction.";
    fetchMock.mockResolvedValue(jsonResponse({ ...report, narrativeAuthority: "StructuredClaims", claims: [{ ...source, text, direction,
      groundingStatus: "StructuredValidated", citations: insufficient ? [] : source.citations,
      semantics: { protocolVersion: "structured-claim-v1", kind, outcome: insufficient ? null : "recall", population: insufficient ? null : "adults",
        exposureOrIntervention: insufficient ? null : "sleep", comparator: insufficient ? null : "wakefulness", timepoint: insufficient ? null : "6 weeks", direction,
        evidenceIds: insufficient ? [] : [source.citations[0].evidenceId], numericEvidenceId: null,
        quantitativeArtifactId: numeric ? report.researchReportId : null, groupKey: numeric ? "group" : null,
        snapshotFingerprint: numeric ? "a".repeat(64) : null, statistic: numeric ? "RandomEffectsHksj" : null,
        numeric: numeric ? { label: "OddsRatio", studyValue: null, artifactValue: 0.73, studyLower: null, studyUpper: null,
          artifactLower: 0.40, artifactUpper: 1.20, confidenceLevel: 0.95, operator: "=", degreesOfFreedom: 2, algorithmVersion: "hksj-v1" } : null
      } }] }));
    renderWithClient(<ResearchReport researchRunId={runId} />);
    expect(await screen.findByText(text)).toBeInTheDocument();
    expect(screen.getByText("Structured validated")).toBeInTheDocument();
    fireEvent.click(screen.getByText("Claim support"));
    expect(screen.getByRole("link", { name: "Evidence and source provenance" })).toHaveAttribute("href", `/research/${runId}/evidence`);
    if (numeric) expect(screen.getByRole("link", { name: /Quantitative artifact:/ })).toHaveAttribute("href", `/research/${runId}/quantitative`);
    expect(screen.queryByText("Legacy claim unverified")).not.toBeInTheDocument();
  });

  it("treats a known run without a report as not ready, not failed", async () => {
    fetchMock.mockResolvedValue(problemResponse(409, "Report is not ready."));

    renderWithClient(<ResearchReport researchRunId={runId} />);

    expect(await screen.findByText("Report not ready")).toBeInTheDocument();
    expect(screen.queryByText(/failed/i)).not.toBeInTheDocument();
  });

  it("distinguishes an unknown run from a report that is still being generated", async () => {
    fetchMock.mockResolvedValue(problemResponse(404, "Research run was not found."));

    renderWithClient(<ResearchReport researchRunId={runId} />);

    expect(await screen.findByText("Research run not found")).toBeInTheDocument();
  });

  it("does not invent identifiers or scientific fields when the API has missing data", async () => {
    fetchMock.mockResolvedValue(jsonResponse(reportResponse({ missingStudyData: true })));

    renderWithClient(<ResearchReport researchRunId={runId} />);

    fireEvent.click(await screen.findByText("Study without identifiers"));

    expect(screen.getAllByText("Not available").length).toBeGreaterThan(0);
    expect(screen.queryByRole("link", { name: /PMID|PMCID|DOI/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/confidence interval/i)).not.toBeInTheDocument();
  });
});

function renderWithClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
}

function problemResponse(status: number, detail: string): Response {
  return new Response(JSON.stringify({ title: status === 409 ? "Report not ready" : "Not found", detail, status }), {
    status,
    headers: { "Content-Type": "application/problem+json" }
  });
}

const runId = "11111111-1111-4111-8111-111111111111";

function reportResponse({ missingStudyData = false }: { missingStudyData?: boolean } = {}) {
  return {
    researchRunId: runId,
    researchReportId: "22222222-2222-4222-8222-222222222222",
    status: "Completed",
    insufficientEvidenceReason: null,
    question: "Does sleep improve recall?",
    executiveSummary: "Executive summary from persisted report.",
    evidenceSummary: "One grounded finding was included.",
    conflictSummary: "No conflict was recorded.",
    limitationsSummary: "The source material is abstract-level.",
    conclusion: "The persisted report supports the claim.",
    synthesisConfidence: "Limited",
    promptVersion: "synthesis-v1",
    narrativeAuthority: "LegacyUnverified",
    generatedAt: "2026-09-28T12:05:00Z",
    coverage: {
      discoveredStudyCount: 1,
      extractedStudyCount: 1,
      evaluatedStudyCount: 1,
      evidenceFindingCount: 1,
      includedStudyCount: 1,
      includedEvidenceFindingCount: 1,
      searchQueryCount: 1,
      studiesWithNoExtractableEvidence: 0,
      studiesWithInsufficientEvaluationSource: 0,
      potentialConflictDetected: false,
      evidenceTruncated: false,
      usesAbstractLevelEvidenceOnly: true,
      searchedSources: ["PubMed"]
    },
    deterministicLimitations: ["Abstract-level evidence only."],
    claims: [
      {
        claimId: "33333333-3333-4333-8333-333333333333",
        claimType: "Conclusion",
        groundingStatus: "LegacyUnverified",
        semantics: null,
        direction: "Positive",
        text: "Sleep improved recall in the cited finding.",
        ordinal: 0,
        citations: [citationResponse({ missingStudyData })]
      }
    ]
  };
}

function citationResponse({ missingStudyData }: { missingStudyData: boolean }) {
  return {
    evidenceId: "44444444-4444-4444-8444-444444444444",
    studyId: "55555555-5555-4555-8555-555555555555",
    pmid: missingStudyData ? null : "12345678",
    pmcid: missingStudyData ? null : "PMC123456",
    doi: missingStudyData ? null : "10.1000/authoritative",
    title: missingStudyData ? "Study without identifiers" : "Authoritative study title",
    journal: missingStudyData ? null : "Journal of Sleep Research",
    publicationYear: missingStudyData ? null : 2026,
    publicationMonth: missingStudyData ? null : 1,
    publicationDay: null,
    publicationTypes: missingStudyData ? [] : ["Journal Article"],
    authors: missingStudyData ? [] : ["Ada Lovelace"],
    studySource: "PubMed",
    outcome: "recall",
    resultSummary: "Recall improved after sleep.",
    supportingText: missingStudyData ? "" : "Extracted supporting text.",
    evidenceDirection: "Positive",
    sourceScope: "Abstract",
    groundingValidated: true,
    population: null,
    exposureOrIntervention: null,
    comparator: null,
    studyDesign: null,
    sampleSize: null,
    effectMeasure: null,
    effectValue: null,
    confidenceIntervalLower: null,
    confidenceIntervalUpper: null,
    confidenceLevel: null,
    reportedStandardError: null,
    pValue: null,
    extractedAt: "2026-09-28T12:03:00Z",
    sourceMaterial: missingStudyData ? null : {
      sourceMaterialId: "66666666-6666-4666-8666-666666666666",
      type: "Abstract",
      provider: "PubMed",
      retrievalMethod: "SearchMetadataAbstract",
      contentVersion: 1,
      retrievedAt: "2026-09-28T12:02:00Z",
      accessStatus: "Unknown",
      wasTruncated: false,
      sectionNames: ["Abstract"]
    },
    ordinal: 0
  };
}
