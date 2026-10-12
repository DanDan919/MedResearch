import type { QuickResearchDetails } from "../../src/quick";

// Synthetic transport fixtures only. These are never imported by application code.
export const quickRunId = "11111111-1111-4111-8111-111111111111";
export const otherQuickRunId = "22222222-2222-4222-8222-222222222222";
export const sourceId = "33333333-3333-4333-8333-333333333333";
export const secondSourceId = "44444444-4444-4444-8444-444444444444";
const pubmedAttempt = "55555555-5555-4555-8555-555555555555";
const europeAttempt = "66666666-6666-4666-8666-666666666666";
const createdAt = "2026-10-12T10:00:00Z";
const finishedAt = "2026-10-12T10:01:00Z";
export const excerpt = "The synthetic source reports limited support in the studied adults.";
export const shortAnswer = "The selected research reports limited support in the studied adults.";

export function quickDetails(runId = quickRunId): QuickResearchDetails {
  const source = { id: sourceId, quickRunId: runId, title: "Synthetic study of sleep in adults", abstract: excerpt,
    pmid: "12345678", pmcid: "PMC1234567", doi: "10.1234/synthetic", journal: "Synthetic Journal", publicationDate: null,
    publicationYear: 2025, authors: ["Synthetic Author"], publicationTypes: ["Journal Article"],
    discoveries: [{ attemptId: pubmedAttempt, provider: "PubMed" as const, providerRecordId: "12345678" }],
    metadataConflicts: [], populationMatch: "MatchedText" as const, url: "https://pubmed.ncbi.nlm.nih.gov/12345678/" };
  return { id: runId, question: "Synthetic question about sleep in adults?", status: "Completed", createdAt, startedAt: createdAt,
    completedAt: finishedAt, safeError: null, result: null, answerStatus: "Succeeded",
    retrieval: { quickRunId: runId, queryVersion: "quick-query-v1", language: "en", queries: ["sleep adults"], outcome: "Selected", isFinal: true,
      sources: [source, { ...source, id: secondSourceId, title: "Synthetic study with different findings", abstract: "The synthetic second source reports different findings in adults.",
        pmid: "23456789", pmcid: null, doi: null, url: "https://pubmed.ncbi.nlm.nih.gov/23456789/",
        discoveries: [{ attemptId: europeAttempt, provider: "EuropePmc", providerRecordId: "MED:23456789" }] }],
      attempts: [{ id: pubmedAttempt, provider: "PubMed", query: "sleep adults", executedQuery: "sleep adults", startedAt: createdAt, finishedAt,
        outcome: "SucceededWithResults", returnedCount: 1, failureCategory: null },
      { id: europeAttempt, provider: "EuropePmc", query: "sleep adults", executedQuery: "sleep adults", startedAt: createdAt, finishedAt,
        outcome: "SucceededWithResults", returnedCount: 1, failureCategory: null }], rejectedCount: 0, limitations: ["Bounded synthetic search."] },
    answer: { quickRunId: runId, shortAnswer, findings: [{ text: shortAnswer, citations: [{ sourceId, excerpt }] }],
      contradictions: [], limitations: ["Only selected abstracts were available."], uncertainty: "Limited", sourceReferences: [sourceId] } };
}

export function quickWithContradiction(): QuickResearchDetails {
  const details = quickDetails();
  details.answer!.contradictions = [{ text: "The selected sources report different findings.", citations: [
    { sourceId, excerpt }, { sourceId: secondSourceId, excerpt: details.retrieval!.sources[1].abstract! }] }];
  details.answer!.sourceReferences.push(secondSourceId);
  return details;
}

export function quickInsufficient(): QuickResearchDetails {
  const details = quickDetails();
  details.answerStatus = "InsufficientEvidence";
  details.answer = { quickRunId, shortAnswer: "The selected sources do not provide enough information for an answer.", findings: [], contradictions: [],
    limitations: ["Available abstracts do not answer this question."], uncertainty: "Insufficient", sourceReferences: [] };
  return details;
}

export function quickProcessing(found = false): QuickResearchDetails {
  const details = quickDetails();
  details.status = "Processing"; details.completedAt = null; details.answer = null;
  details.answerStatus = found ? "Started" : null;
  if (!found) details.retrieval = null;
  return details;
}

export function quickHistory(details = quickDetails()) {
  return { items: [{ id: details.id, question: details.question, status: details.status, createdAt: details.createdAt, completedAt: details.completedAt }],
    page: 1, pageSize: 20, totalCount: 1 };
}
