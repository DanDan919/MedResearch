import { expect, test, type Page } from "@playwright/test";

const runId = "11111111-1111-4111-8111-111111111111";

test("report workspace exposes persisted claims and expandable evidence", async ({ page }) => {
  await mockReportApi(page, reportResponse());
  await page.goto(`/research/${runId}/report`);

  await expect(page.getByRole("heading", { name: "Research Report" })).toBeVisible();
  await expect(page.getByText("Executive summary from persisted report.")).toBeVisible();
  await expect(page.getByText("Authoritative study title")).toBeVisible();

  await page.getByText("Authoritative study title").click();

  await expect(page.getByRole("heading", { name: "Extracted supporting text" })).toBeVisible();
  await expect(page.getByRole("link", { name: /PMID: 12345678/i })).toHaveAttribute(
    "href",
    "https://pubmed.ncbi.nlm.nih.gov/12345678/"
  );
  await expect(page.getByText(/Abstract from PubMed via SearchMetadataAbstract/)).toBeVisible();
});

test("report workspace presents a not-ready state for a known run without a report", async ({ page }) => {
  await mockReportApi(page, null, 409);
  await page.goto(`/research/${runId}/report`);

  await expect(page.getByText("Report not ready")).toBeVisible();
  await expect(page.getByText(/failed/i)).not.toBeVisible();
});

test("report workspace distinguishes an unauthenticated response", async ({ page }) => {
  await mockReportApi(page, { title: "Unauthorized", status: 401 }, 401);
  await page.goto(`/research/${runId}/report`);

  await expect(page.getByText("Authentication required")).toBeVisible();
  await expect(page.getByText(/Sign in to view this research report/i)).toBeVisible();
});

test("report workspace keeps missing study identifiers absent", async ({ page }) => {
  await mockReportApi(page, reportResponse(true));
  await page.goto(`/research/${runId}/report`);
  await page.getByText("Study without identifiers").click();

  await expect(page.getByText("Not available").first()).toBeVisible();
  await expect(page.getByRole("link", { name: /PMID|PMCID|DOI/i })).toHaveCount(0);
});

async function mockReportApi(page: Page, report?: unknown, status = 200) {
  await page.addInitScript(
    ({ report: responseBody, responseStatus }) => {
      const originalFetch = window.fetch.bind(window);
      window.fetch = async (input, init) => {
        const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);

        if (url.includes("/health/ready")) return new Response("Healthy", { status: 200 });
        if (url.endsWith("/report")) {
          return new Response(
            responseBody === null ? JSON.stringify({ title: "Report not ready", status: responseStatus }) : JSON.stringify(responseBody),
            { status: responseStatus, headers: { "Content-Type": responseStatus === 200 ? "application/json" : "application/problem+json" } }
          );
        }

        return originalFetch(input, init);
      };
    },
    { report, responseStatus: status }
  );
}

function reportResponse(missingStudyData = false) {
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
    claims: [{
      claimId: "33333333-3333-4333-8333-333333333333",
      claimType: "Conclusion",
      direction: "Positive",
      text: "Sleep improved recall in the cited finding.",
      ordinal: 0,
      citations: [{
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
        studySource: missingStudyData ? null : "PubMed",
        outcome: "recall",
        resultSummary: missingStudyData ? null : "Recall improved after sleep.",
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
      }]
    }]
  };
}
