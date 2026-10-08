import { describe, expect, it } from "vitest";
import { researchClaimSemanticsSchema, researchReportClaimSchema } from "../src/schemas";

const evidenceId = "11111111-1111-4111-8111-111111111111";
const base = {
  protocolVersion: "structured-claim-v1", kind: "QualitativeEffect", outcome: "recall", population: "adults",
  exposureOrIntervention: "sleep", comparator: "wakefulness", timepoint: "6 weeks", direction: "Positive",
  evidenceIds: [evidenceId], numericEvidenceId: null, quantitativeArtifactId: null, groupKey: null,
  snapshotFingerprint: null, statistic: null, numeric: null
};

describe("structured claim contract", () => {
  it("preserves backend scope without deriving new scientific text", () => {
    expect(researchClaimSemanticsSchema.parse(base)).toEqual(base);
  });
  it.each(["kind", "direction", "statistic"])("rejects unknown %s", (field) => {
    expect(researchClaimSemanticsSchema.safeParse({ ...base, [field]: "999" }).success).toBe(false);
  });
  it("does not upgrade missing or contradictory grounding", () => {
    const claim = { claimId: evidenceId, claimType: "Conclusion", direction: "Positive", text: "Backend text", ordinal: 0, citations: [] };
    expect(researchReportClaimSchema.safeParse(claim).success).toBe(false);
    expect(researchReportClaimSchema.safeParse({ ...claim, groundingStatus: "StructuredValidated", semantics: null }).success).toBe(false);
    expect(researchReportClaimSchema.safeParse({ ...claim, groundingStatus: "LegacyUnverified", semantics: base }).success).toBe(false);
    expect(researchReportClaimSchema.parse({ ...claim, groundingStatus: "LegacyUnverified", semantics: null }).semantics).toBeNull();
  });
  it("rejects arbitrary numeric fields and invalid artifact identities", () => {
    expect(researchClaimSemanticsSchema.safeParse({ ...base, pooledEstimate: 0.63 }).success).toBe(false);
    expect(researchClaimSemanticsSchema.safeParse({ ...base, kind: "QuantitativeSynthesis", direction: "NotApplicable", quantitativeArtifactId: "not-an-id" }).success).toBe(false);
  });
  it("preserves interval method and values exactly and rejects non-finite values", () => {
    const numeric = { label: "OddsRatio", studyValue: null, artifactValue: 0.73, studyLower: null, studyUpper: null,
      artifactLower: 0.30, artifactUpper: 1.40, confidenceLevel: 0.95, operator: "=", degreesOfFreedom: 2, algorithmVersion: "pi-v1" };
    const proposal = { ...base, kind: "QuantitativeSynthesis", direction: "NotApplicable", quantitativeArtifactId: evidenceId, groupKey: "group",
      snapshotFingerprint: "a".repeat(64), statistic: "RandomEffectsPredictionInterval", numeric };
    expect(researchClaimSemanticsSchema.parse(proposal).numeric).toEqual(numeric);
    expect(researchClaimSemanticsSchema.safeParse({ ...proposal, numeric: { ...numeric, artifactValue: Infinity } }).success).toBe(false);
  });
});
