import { z } from "zod";

export const researchRunStatusSchema = z.enum([
  "Queued",
  "Planning",
  "Searching",
  "Extracting",
  "Evaluating",
  "Synthesizing",
  "Completed",
  "Failed",
  "Cancelled"
]);

export const createResearchResponseSchema = z.object({
  researchRunId: z.string().uuid(),
  status: researchRunStatusSchema
});

export const researchRunResponseSchema = z.object({
  researchRunId: z.string().uuid(),
  question: z.string(),
  status: researchRunStatusSchema,
  createdAt: z.string(),
  startedAt: z.string().nullable(),
  completedAt: z.string().nullable(),
  failureReason: z.string().nullable()
});

export const researchRunSummaryResponseSchema = z.object({
  researchRunId: z.string().uuid(),
  researchQuestionId: z.string().uuid(),
  question: z.string(),
  status: researchRunStatusSchema,
  createdAt: z.string(),
  startedAt: z.string().nullable(),
  completedAt: z.string().nullable(),
  failureReason: z.string().nullable()
});

export const researchRunListResponseSchema = z.object({
  items: z.array(researchRunSummaryResponseSchema),
  page: z.number(),
  pageSize: z.number(),
  totalCount: z.number(),
  totalPages: z.number()
});

export const researchRunProgressMetricSchema = z.object({
  label: z.string(),
  value: z.number()
});

export const researchRunStageProgressSchema = z.object({
  stage: researchRunStatusSchema,
  state: z.enum(["Pending", "Current", "Completed"]),
  metrics: z.array(researchRunProgressMetricSchema)
});

export const researchRunProgressResponseSchema = z.object({
  researchRunId: z.string().uuid(),
  question: z.string(),
  status: researchRunStatusSchema,
  createdAt: z.string(),
  startedAt: z.string().nullable(),
  completedAt: z.string().nullable(),
  failureReason: z.string().nullable(),
  refreshedAt: z.string(),
  processing: z.object({
    leaseState: z.enum(["None", "Active", "Expired", "Terminal"]),
    leaseExpiresAt: z.string().nullable(),
    lastHeartbeatAt: z.string().nullable(),
    leaseVersion: z.number()
  }),
  metrics: z.object({
    researchPlanCount: z.number(),
    plannedSearchQueryCount: z.number(),
    literatureSearchCount: z.number(),
    literatureSearchSourceCount: z.number(),
    literatureSearchResultCount: z.number(),
    discoveryPathCount: z.number(),
    distinctDiscoveredStudyCount: z.number(),
    currentSourceMaterialCount: z.number(),
    structuredFullTextMaterialCount: z.number(),
    abstractMaterialCount: z.number(),
    evidenceExtractionCount: z.number(),
    completedEvidenceExtractionCount: z.number(),
    skippedEvidenceExtractionCount: z.number(),
    evidenceFindingCount: z.number(),
    evidenceEvaluationCount: z.number(),
    completedEvidenceEvaluationCount: z.number(),
    skippedEvidenceEvaluationCount: z.number(),
    researchReportCount: z.number(),
    researchReportClaimCount: z.number()
  }),
  stages: z.array(researchRunStageProgressSchema)
});

export const researchReportCitationSchema = z.object({
  evidenceId: z.string().uuid(),
  studyId: z.string().uuid(),
  pmid: z.string().nullable(),
  pmcid: z.string().nullable(),
  doi: z.string().nullable(),
  title: z.string(),
  supportingText: z.string(),
  evidenceDirection: z.string(),
  ordinal: z.number()
});

export const researchReportClaimSchema = z.object({
  claimId: z.string().uuid(),
  claimType: z.string(),
  direction: z.string(),
  text: z.string(),
  ordinal: z.number(),
  citations: z.array(researchReportCitationSchema)
});

export const researchReportCoverageSchema = z.object({
  discoveredStudyCount: z.number(),
  extractedStudyCount: z.number(),
  evaluatedStudyCount: z.number(),
  evidenceFindingCount: z.number(),
  includedStudyCount: z.number(),
  includedEvidenceFindingCount: z.number(),
  searchQueryCount: z.number(),
  studiesWithNoExtractableEvidence: z.number(),
  studiesWithInsufficientEvaluationSource: z.number(),
  potentialConflictDetected: z.boolean(),
  evidenceTruncated: z.boolean(),
  usesAbstractLevelEvidenceOnly: z.boolean(),
  searchedSources: z.array(z.string())
});

export const researchReportResponseSchema = z.object({
  researchRunId: z.string().uuid(),
  researchReportId: z.string().uuid(),
  status: z.string(),
  insufficientEvidenceReason: z.string().nullable(),
  question: z.string(),
  executiveSummary: z.string(),
  evidenceSummary: z.string(),
  conflictSummary: z.string(),
  limitationsSummary: z.string(),
  conclusion: z.string(),
  synthesisConfidence: z.string(),
  promptVersion: z.string(),
  generatedAt: z.string(),
  coverage: researchReportCoverageSchema,
  deterministicLimitations: z.array(z.string()),
  claims: z.array(researchReportClaimSchema)
});

export const problemDetailsSchema = z
  .object({
    type: z.string().optional(),
    title: z.string().optional(),
    status: z.number().optional(),
    detail: z.string().optional(),
    instance: z.string().optional()
  })
  .catchall(z.unknown());
