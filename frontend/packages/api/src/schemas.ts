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
