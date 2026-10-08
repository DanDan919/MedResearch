import { expect, test, type Page } from "@playwright/test";

const runId = "11111111-1111-4111-8111-111111111111";

test("evidence workspace shows multi-search provenance and honest zero-evidence state", async ({ page }) => {
  await mockProvenanceApi(page, provenanceResponse(false));
  await page.goto(`/research/${runId}/evidence`);

  await expect(page.getByRole("heading", { name: "Evidence & Provenance" })).toBeVisible();
  await expect(page.getByText("Discovery paths (2)")).toBeVisible();
  await expect(page.getByText("No validated Evidence is available for this extraction.")).toBeVisible();
  await expect(page.getByText("raw source body", { exact: false })).not.toBeVisible();
  await expect(page.getByText("No provider attempts recorded.", { exact: false })).toBeVisible();
});

test("partial provider failure remains visible beside persisted studies", async ({ page }) => {
  const response = provenanceResponse(false);
  response.providerAttempts = [
    { attemptId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", researchPlanId: runId, source: "PubMed", query: "sleep recall", status: "SucceededWithResults", resultCount: 1, failureCategory: null, startedAt: response.createdAt, completedAt: response.completedAt, literatureSearchId: response.searches[0].literatureSearchId },
    { attemptId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", researchPlanId: runId, source: "EuropePmc", query: "sleep recall", status: "Failed", resultCount: null, failureCategory: "NetworkFailure", startedAt: response.createdAt, completedAt: response.completedAt, literatureSearchId: null }
  ];
  response.coverage.hasPersistedProviderFailureProvenance = true;
  response.searches = response.searches.slice(0, 1);
  response.studies[0].discoveryPaths = response.studies[0].discoveryPaths.slice(0, 1);
  await mockProvenanceApi(page, response);
  await page.goto(`/research/${runId}/evidence`);
  const coverage = page.getByRole("region", { name: "Provider coverage" });
  await expect(coverage.getByText("PubMed", { exact: true })).toBeVisible();
  await expect(coverage.getByText("Succeeded", { exact: true })).toBeVisible();
  await expect(coverage.getByText("EuropePmc", { exact: true })).toBeVisible();
  await expect(coverage.getByText("Failed", { exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Sleep and recall" })).toBeVisible();
});

test("evidence workspace links claims and quantitative contributions to persisted evidence", async ({ page }) => {
  await mockProvenanceApi(page, provenanceResponse(true));
  await page.goto(`/research/${runId}/evidence`);

  await expect(page.getByText("Persisted supporting excerpt.")).toBeVisible();
  await expect(page.getByRole("link", { name: /Evidence 77777777/ }).first()).toHaveAttribute("href", /#evidence-77777777/);
  await expect(page.getByText("Quantitative contribution lineage")).toBeVisible();
  await expect(page.getByText(/Content is intentionally not displayed/)).toBeVisible();
});

async function mockProvenanceApi(page: Page, response: unknown) {
  await page.addInitScript(
    ({ responseBody }) => {
      const originalFetch = window.fetch.bind(window);
      window.fetch = async (input, init) => {
        const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
        if (url.endsWith("/provenance")) return new Response(JSON.stringify(responseBody), { status: 200, headers: { "Content-Type": "application/json" } });
        if (url.includes("/health/ready")) return new Response("Healthy", { status: 200 });
        return originalFetch(input, init);
      };
    },
    { responseBody: response }
  );
}

function provenanceResponse(withEvidence: boolean) {
  const evidenceId = "77777777-7777-4777-8777-777777777777";
  const studyId = "55555555-5555-4555-8555-555555555555";
  const extractionId = "66666666-6666-4666-8666-666666666666";
  const sourceMaterialId = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";
  const now = "2026-09-28T12:00:00Z";
  return {
    researchRunId: runId,
    question: "Does sleep improve recall?",
    status: "Completed",
    createdAt: now,
    startedAt: now,
    completedAt: now,
    coverage: { researchPlanCount: 1, literatureSearchCount: 2, discoveryPathCount: 2, distinctStudyCount: 1, sourceMaterialCount: 1, evidenceExtractionCount: 1, evidenceFindingCount: withEvidence ? 1 : 0, evidenceEvaluationCount: 0, researchReportClaimCount: withEvidence ? 1 : 0, hasPersistedProviderFailureProvenance: false },
    plans: [],
    providerAttempts: [] as Array<{ attemptId: string; researchPlanId: string; source: string; query: string; status: string; resultCount: number | null; failureCategory: string | null; startedAt: string; completedAt: string | null; literatureSearchId: string | null }>,
    searches: [
      { literatureSearchId: "99999999-9999-4999-8999-999999999999", researchPlanId: null, source: "PubMed", query: "sleep recall", searchedAt: now, resultCount: 1, persistedStudyCount: 1, duplicateStudyCount: 0, resultStatus: "SucceededWithResults" },
      { literatureSearchId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", researchPlanId: null, source: "EuropePmc", query: "sleep recall", searchedAt: now, resultCount: 1, persistedStudyCount: 1, duplicateStudyCount: 0, resultStatus: "SucceededWithResults" }
    ],
    studies: [{
      studyId,
      title: "Sleep and recall",
      pmid: "12345678",
      pmcid: "PMC123456",
      doi: "10.1000/sleep",
      journal: "Journal",
      publicationYear: 2026,
      publicationMonth: 1,
      publicationDay: null,
      publicationTypes: ["Journal Article"],
      authors: ["Ada Lovelace"],
      source: "PubMed",
      discoveryPaths: [
        { researchStudyDiscoveryId: "88888888-8888-4888-8888-888888888888", literatureSearchId: "99999999-9999-4999-8999-999999999999", source: "PubMed", sourceStudyIdentifier: "12345678", query: "sleep recall", searchedAt: now, discoveredAt: now },
        { researchStudyDiscoveryId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", literatureSearchId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", source: "EuropePmc", sourceStudyIdentifier: "MED:12345678", query: "sleep recall", searchedAt: now, discoveredAt: now }
      ],
      sourceMaterials: [{ sourceMaterialId, studyId, type: "Abstract", provider: "PubMed", providerSourceId: "12345678", retrievalMethod: "SearchMetadataAbstract", contentHash: "hash", contentVersion: 1, retrievedAt: now, sourceUpdatedAt: null, accessStatus: "Unknown", characterCount: 42, wasTruncated: false, isCurrent: true, sectionNames: ["Abstract"] }],
      extractions: [{ evidenceExtractionId: extractionId, studyId, sourceMaterialId, status: "Completed", skipReason: null, sourceScope: "Abstract", provider: "FakeLLM", model: "fake-model", promptVersion: "extract-v1", extractedAt: now, evidenceCount: withEvidence ? 1 : 0, groundingValidated: withEvidence }],
      evidence: withEvidence ? [{ evidenceId, evidenceExtractionId: extractionId, outcome: "recall", resultSummary: "Recall improved.", supportingText: "Persisted supporting excerpt.", direction: "Positive", sourceScope: "Abstract", extractedAt: now, groundingValidated: true, population: "adults", exposureOrIntervention: "sleep", comparator: "wakefulness", studyDesign: "trial", sampleSize: 120, effectMeasure: null, effectValue: null, confidenceIntervalLower: null, confidenceIntervalUpper: null, confidenceLevel: null, reportedStandardError: null, pValue: null }] : [],
      evaluations: []
    }],
    reportClaims: withEvidence ? [{ researchReportId: "dddddddd-dddd-4ddd-8ddd-dddddddddddd", researchReportClaimId: "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee", claimType: "Conclusion", direction: "Positive", text: "Sleep improved recall.", ordinal: 0, evidenceIds: [evidenceId] }] : [],
    quantitativeContributions: withEvidence ? [{ artifactId: "ffffffff-ffff-4fff-8fff-ffffffffffff", groupKey: "recall", analysisMethod: "Fixed", ordinal: 0, evidenceId, studyId, evidenceExtractionId: extractionId, sourceMaterialId }] : []
  };
}
