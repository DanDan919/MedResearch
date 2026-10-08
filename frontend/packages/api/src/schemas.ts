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

export const researchReportSourceMaterialSchema = z.object({
  sourceMaterialId: z.string().uuid(),
  type: z.string(),
  provider: z.string(),
  retrievalMethod: z.string(),
  contentVersion: z.number(),
  retrievedAt: z.string(),
  accessStatus: z.string(),
  wasTruncated: z.boolean(),
  sectionNames: z.array(z.string())
});

export const researchReportCitationSchema = z.object({
  evidenceId: z.string().uuid(),
  studyId: z.string().uuid(),
  pmid: z.string().nullable(),
  pmcid: z.string().nullable(),
  doi: z.string().nullable(),
  title: z.string(),
  journal: z.string().nullable(),
  publicationYear: z.number().nullable(),
  publicationMonth: z.number().nullable(),
  publicationDay: z.number().nullable(),
  publicationTypes: z.array(z.string()),
  authors: z.array(z.string()),
  studySource: z.string().nullable(),
  outcome: z.string(),
  resultSummary: z.string().nullable(),
  supportingText: z.string(),
  evidenceDirection: z.string(),
  sourceScope: z.string(),
  groundingValidated: z.boolean(),
  population: z.string().nullable(),
  exposureOrIntervention: z.string().nullable(),
  comparator: z.string().nullable(),
  studyDesign: z.string().nullable(),
  sampleSize: z.number().nullable(),
  effectMeasure: z.string().nullable(),
  effectValue: z.number().nullable(),
  confidenceIntervalLower: z.number().nullable(),
  confidenceIntervalUpper: z.number().nullable(),
  confidenceLevel: z.number().nullable(),
  reportedStandardError: z.number().nullable(),
  pValue: z.number().nullable(),
  extractedAt: z.string(),
  sourceMaterial: researchReportSourceMaterialSchema.nullable(),
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

const quantitativeSynthesisContributionSchema = z.object({
  evidenceId: z.string().uuid(),
  studyId: z.string().uuid(),
  evidenceExtractionId: z.string().uuid(),
  sourceMaterialId: z.string().uuid(),
  analysisScaleEffect: z.number(),
  analysisScaleVariance: z.number(),
  analysisScaleStandardError: z.number(),
  weight: z.number(),
  normalizedWeight: z.number()
});

const quantitativeHeterogeneityDiagnosticsSchema = z.object({
  cochransQ: z.number(),
  degreesOfFreedom: z.number().int(),
  iSquared: z.number(),
  studyCount: z.number().int(),
  algorithmVersion: z.string()
});

const betweenStudyVarianceEstimateSchema = z.object({
  tauSquared: z.number().nullable(),
  estimator: z.number().int(),
  status: z.number().int(),
  algorithmVersion: z.string(),
  studyCount: z.number().int(),
  converged: z.boolean(),
  iterationCount: z.number().int(),
  failureReason: z.number().int().nullable()
});

const quantitativeHksjInferenceSchema = z.object({
  status: z.number().int(),
  confidenceIntervalMethod: z.number().int(),
  algorithmVersion: z.string(),
  outputConfidenceLevel: z.number(),
  studyCount: z.number().int(),
  degreesOfFreedom: z.number().int().nullable(),
  varianceAdjustment: z.number().nullable(),
  criticalValue: z.number().nullable(),
  analysisScaleEffect: z.number().nullable(),
  analysisScaleVariance: z.number().nullable(),
  analysisScaleStandardError: z.number().nullable(),
  analysisScaleConfidenceIntervalLower: z.number().nullable(),
  analysisScaleConfidenceIntervalUpper: z.number().nullable(),
  reportedScaleEffect: z.number().nullable(),
  reportedScaleConfidenceIntervalLower: z.number().nullable(),
  reportedScaleConfidenceIntervalUpper: z.number().nullable(),
  failureReasons: z.array(z.number().int())
});

const quantitativePredictionIntervalSchema = z.object({
  status: z.number().int(),
  method: z.number().int(),
  algorithmVersion: z.string(),
  outputConfidenceLevel: z.number(),
  studyCount: z.number().int(),
  degreesOfFreedom: z.number().int().nullable(),
  tauSquared: z.number().nullable(),
  summaryEffectVariance: z.number().nullable(),
  summaryEffectStandardError: z.number().nullable(),
  predictionVariance: z.number().nullable(),
  predictionStandardError: z.number().nullable(),
  criticalValue: z.number().nullable(),
  analysisScaleEffect: z.number().nullable(),
  analysisScaleLower: z.number().nullable(),
  analysisScaleUpper: z.number().nullable(),
  reportedScaleEffect: z.number().nullable(),
  reportedScaleLower: z.number().nullable(),
  reportedScaleUpper: z.number().nullable(),
  failureReasons: z.array(z.number().int())
});

const quantitativeRandomEffectsSchema = z.object({
  status: z.number().int(),
  method: z.number().int(),
  algorithmVersion: z.string(),
  confidenceIntervalMethod: z.number().int(),
  outputConfidenceLevel: z.number(),
  tauSquared: z.number().nullable(),
  tauSquaredEstimator: z.number().int(),
  tauSquaredAlgorithmVersion: z.string(),
  studyCount: z.number().int(),
  analysisScaleEffect: z.number().nullable(),
  analysisScaleVariance: z.number().nullable(),
  analysisScaleStandardError: z.number().nullable(),
  analysisScaleConfidenceIntervalLower: z.number().nullable(),
  analysisScaleConfidenceIntervalUpper: z.number().nullable(),
  reportedScaleEffect: z.number().nullable(),
  reportedScaleConfidenceIntervalLower: z.number().nullable(),
  reportedScaleConfidenceIntervalUpper: z.number().nullable(),
  hksjInference: quantitativeHksjInferenceSchema.nullable(),
  predictionInterval: quantitativePredictionIntervalSchema.nullable(),
  contributions: z.array(quantitativeSynthesisContributionSchema),
  failureReasons: z.array(z.number().int())
});

const quantitativeSynthesisResultSchema = z.object({
  researchRunId: z.string().uuid(),
  groupKey: z.string(),
  outcomeGroupKey: z.string(),
  populationCompatibilityKey: z.string(),
  comparatorCompatibilityKey: z.string(),
  studyDesignCompatibilityKey: z.string(),
  effectMeasureType: z.number().int(),
  status: z.number().int(),
  method: z.number().int(),
  algorithmVersion: z.string(),
  outputConfidenceLevel: z.number(),
  evidenceCount: z.number().int(),
  uniqueStudyCount: z.number().int(),
  analysisScaleEffect: z.number().nullable(),
  analysisScaleVariance: z.number().nullable(),
  analysisScaleStandardError: z.number().nullable(),
  analysisScaleConfidenceIntervalLower: z.number().nullable(),
  analysisScaleConfidenceIntervalUpper: z.number().nullable(),
  reportedScaleEffect: z.number().nullable(),
  reportedScaleConfidenceIntervalLower: z.number().nullable(),
  reportedScaleConfidenceIntervalUpper: z.number().nullable(),
  heterogeneityDiagnostics: quantitativeHeterogeneityDiagnosticsSchema.nullable(),
  betweenStudyVariance: betweenStudyVarianceEstimateSchema.nullable(),
  randomEffects: quantitativeRandomEffectsSchema.nullable(),
  contributions: z.array(quantitativeSynthesisContributionSchema),
  rejectionReasons: z.array(z.number().int())
});

export const quantitativeSynthesisArtifactResponseSchema = z.object({
  artifactId: z.string().uuid(),
  persistedAt: z.string(),
  snapshotFingerprint: z.string(),
  result: quantitativeSynthesisResultSchema
});

const researchProvenanceCoverageSchema = z.object({
  researchPlanCount: z.number().int(),
  literatureSearchCount: z.number().int(),
  discoveryPathCount: z.number().int(),
  distinctStudyCount: z.number().int(),
  sourceMaterialCount: z.number().int(),
  evidenceExtractionCount: z.number().int(),
  evidenceFindingCount: z.number().int(),
  evidenceEvaluationCount: z.number().int(),
  researchReportClaimCount: z.number().int(),
  hasPersistedProviderFailureProvenance: z.boolean()
});

const researchPlanProvenanceSchema = z.object({
  researchPlanId: z.string().uuid(),
  originalQuestion: z.string(),
  searchQueries: z.array(z.string()),
  provider: z.string(),
  model: z.string(),
  promptVersion: z.string(),
  generatedAt: z.string()
});

const literatureSearchProvenanceSchema = z.object({
  literatureSearchId: z.string().uuid(),
  researchPlanId: z.string().uuid().nullable(),
  source: z.string(),
  query: z.string(),
  searchedAt: z.string(),
  resultCount: z.number().int(),
  persistedStudyCount: z.number().int(),
  duplicateStudyCount: z.number().int(),
  resultStatus: z.string()
});

const studyDiscoveryProvenanceSchema = z.object({
  researchStudyDiscoveryId: z.string().uuid(),
  literatureSearchId: z.string().uuid(),
  source: z.string(),
  sourceStudyIdentifier: z.string().nullable(),
  query: z.string(),
  searchedAt: z.string(),
  discoveredAt: z.string()
});

const sourceMaterialProvenanceSchema = z.object({
  sourceMaterialId: z.string().uuid(),
  studyId: z.string().uuid(),
  type: z.string(),
  provider: z.string(),
  providerSourceId: z.string().nullable(),
  retrievalMethod: z.string(),
  contentHash: z.string(),
  contentVersion: z.number().int(),
  retrievedAt: z.string(),
  sourceUpdatedAt: z.string().nullable(),
  accessStatus: z.string(),
  characterCount: z.number().int(),
  wasTruncated: z.boolean(),
  isCurrent: z.boolean(),
  sectionNames: z.array(z.string())
});

const evidenceExtractionProvenanceSchema = z.object({
  evidenceExtractionId: z.string().uuid(),
  studyId: z.string().uuid(),
  sourceMaterialId: z.string().uuid().nullable(),
  status: z.string(),
  skipReason: z.string().nullable(),
  sourceScope: z.string(),
  provider: z.string().nullable(),
  model: z.string().nullable(),
  promptVersion: z.string(),
  extractedAt: z.string(),
  evidenceCount: z.number().int(),
  groundingValidated: z.boolean()
});

const evidenceProvenanceSchema = z.object({
  evidenceId: z.string().uuid(),
  evidenceExtractionId: z.string().uuid(),
  outcome: z.string(),
  resultSummary: z.string(),
  supportingText: z.string(),
  direction: z.string(),
  sourceScope: z.string(),
  extractedAt: z.string(),
  groundingValidated: z.boolean(),
  population: z.string().nullable(),
  exposureOrIntervention: z.string().nullable(),
  comparator: z.string().nullable(),
  studyDesign: z.string().nullable(),
  sampleSize: z.number().int().nullable(),
  effectMeasure: z.string().nullable(),
  effectValue: z.number().nullable(),
  confidenceIntervalLower: z.number().nullable(),
  confidenceIntervalUpper: z.number().nullable(),
  confidenceLevel: z.number().nullable(),
  reportedStandardError: z.number().nullable(),
  pValue: z.number().nullable()
});

const evidenceEvaluationProvenanceSchema = z.object({
  evidenceEvaluationId: z.string().uuid(),
  studyId: z.string().uuid(),
  status: z.string(),
  skipReason: z.string().nullable(),
  sourceScope: z.string(),
  evidenceIds: z.array(z.string().uuid()),
  evaluatorProvider: z.string().nullable(),
  evaluatorModel: z.string().nullable(),
  promptVersion: z.string(),
  evaluatedAt: z.string(),
  studyDesign: z.string(),
  sampleInformation: z.string(),
  comparatorPresence: z.string(),
  comparatorDescription: z.string().nullable(),
  randomization: z.string(),
  blinding: z.string(),
  allocationConcealment: z.string(),
  attritionMissingData: z.string(),
  precision: z.string(),
  directness: z.string(),
  overallConfidence: z.string(),
  rationale: z.string(),
  reportingLimitations: z.array(z.string()),
  authorReportedLimitations: z.array(z.string()),
  hasSampleSize: z.boolean(),
  hasEffectEstimate: z.boolean(),
  hasConfidenceInterval: z.boolean(),
  hasPValue: z.boolean(),
  hasComparator: z.boolean(),
  unknownDomainCount: z.number().int(),
  insufficientSourceDomainCount: z.number().int()
});

const researchReportClaimProvenanceSchema = z.object({
  researchReportId: z.string().uuid(),
  researchReportClaimId: z.string().uuid(),
  claimType: z.string(),
  direction: z.string(),
  text: z.string(),
  ordinal: z.number().int(),
  evidenceIds: z.array(z.string().uuid())
});

const quantitativeContributionProvenanceSchema = z.object({
  artifactId: z.string().uuid(),
  groupKey: z.string(),
  analysisMethod: z.string(),
  ordinal: z.number().int(),
  evidenceId: z.string().uuid(),
  studyId: z.string().uuid(),
  evidenceExtractionId: z.string().uuid(),
  sourceMaterialId: z.string().uuid()
});

export const literatureProviderAttemptSchema = z.object({
  attemptId: z.string().uuid(),
  researchPlanId: z.string().uuid(),
  source: z.enum(["PubMed", "EuropePmc"]),
  query: z.string().min(1).max(2000),
  status: z.enum(["Started", "SucceededWithResults", "SucceededZeroResults", "Failed", "TimedOut", "Cancelled"]),
  resultCount: z.number().int().nonnegative().nullable(),
  failureCategory: z.enum(["NetworkFailure", "Timeout", "RateLimited", "InvalidResponse", "ProviderProtocolError", "ResponseTooLarge", "Cancelled", "UnexpectedFailure"]).nullable(),
  startedAt: z.string().datetime({ offset: true }),
  completedAt: z.string().datetime({ offset: true }).nullable(),
  literatureSearchId: z.string().uuid().nullable()
}).superRefine((attempt, context) => {
  const success = attempt.status === "SucceededWithResults" || attempt.status === "SucceededZeroResults";
  const valid = attempt.status === "Started"
    ? attempt.completedAt === null && attempt.resultCount === null && attempt.failureCategory === null && attempt.literatureSearchId === null
    : success
      ? attempt.completedAt !== null && attempt.failureCategory === null && attempt.literatureSearchId !== null &&
        (attempt.status === "SucceededZeroResults" ? attempt.resultCount === 0 : attempt.resultCount !== null && attempt.resultCount > 0)
      : attempt.completedAt !== null && attempt.resultCount === null && attempt.literatureSearchId === null && attempt.failureCategory !== null &&
        (attempt.status === "TimedOut" ? attempt.failureCategory === "Timeout" : attempt.status === "Cancelled" ? attempt.failureCategory === "Cancelled" : !["Timeout", "Cancelled"].includes(attempt.failureCategory));
  if (!valid || (attempt.completedAt !== null && Date.parse(attempt.completedAt) < Date.parse(attempt.startedAt))) {
    context.addIssue({ code: "custom", message: "Inconsistent provider attempt outcome" });
  }
});

export const researchProvenanceResponseSchema = z.object({
  researchRunId: z.string().uuid(),
  question: z.string(),
  status: z.string(),
  createdAt: z.string(),
  startedAt: z.string().nullable(),
  completedAt: z.string().nullable(),
  coverage: researchProvenanceCoverageSchema,
  plans: z.array(researchPlanProvenanceSchema),
  searches: z.array(literatureSearchProvenanceSchema),
  providerAttempts: z.array(literatureProviderAttemptSchema),
  studies: z.array(z.object({
    studyId: z.string().uuid(),
    title: z.string(),
    pmid: z.string().nullable(),
    pmcid: z.string().nullable(),
    doi: z.string().nullable(),
    journal: z.string().nullable(),
    publicationYear: z.number().int().nullable(),
    publicationMonth: z.number().int().nullable(),
    publicationDay: z.number().int().nullable(),
    publicationTypes: z.array(z.string()),
    authors: z.array(z.string()),
    source: z.string(),
    discoveryPaths: z.array(studyDiscoveryProvenanceSchema),
    sourceMaterials: z.array(sourceMaterialProvenanceSchema),
    extractions: z.array(evidenceExtractionProvenanceSchema),
    evidence: z.array(evidenceProvenanceSchema),
    evaluations: z.array(evidenceEvaluationProvenanceSchema)
  })),
  reportClaims: z.array(researchReportClaimProvenanceSchema),
  quantitativeContributions: z.array(quantitativeContributionProvenanceSchema)
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
