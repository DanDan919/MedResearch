import type { QuantitativeSynthesisArtifactResponse } from "@medresearch/api";

export type QuantitativeArtifact = QuantitativeSynthesisArtifactResponse;
export type QuantitativeResult = QuantitativeArtifact["result"];
export type QuantitativeContribution = QuantitativeResult["contributions"][number];
export type RandomEffectsResult = NonNullable<QuantitativeResult["randomEffects"]>;

const effectMeasureLabels: Record<number, string> = {
  0: "Unknown effect measure",
  1: "Odds ratio",
  2: "Risk ratio",
  3: "Hazard ratio",
  4: "Risk difference",
  5: "Mean difference",
  6: "Standardized mean difference",
  7: "Correlation",
  8: "Regression coefficient",
  9: "Proportion",
  10: "Other effect measure"
};

const rejectionReasonLabels: Record<number, string> = {
  0: "Group was not ready for meta-analysis input",
  1: "Insufficient independent studies",
  2: "Dependent evidence from the same study",
  3: "Unsupported effect measure",
  4: "Missing eligible evidence",
  5: "Missing normalized effect",
  6: "Missing variance",
  7: "Invalid variance",
  8: "Invalid weight",
  9: "Non-finite pooled effect",
  10: "Non-finite confidence interval",
  11: "Back-transformation failed",
  12: "Duplicate evidence contribution",
  13: "Non-finite heterogeneity diagnostics"
};

export function effectMeasureLabel(value: number): string {
  return effectMeasureLabels[value] ?? `Effect measure code ${value}`;
}

export function statusLabel(status: number): string {
  return status === 1 ? "Synthesized" : "Not estimated";
}

export function methodLabel(method: number): string {
  if (method === 0) return "Common-effect inverse variance";
  if (method === 1) return "Random-effects inverse variance";
  return `Method code ${method}`;
}

export function formatNumber(value: number | null | undefined, digits = 3): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return "Not available";
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: digits }).format(value);
}

export function formatInterval(lower: number | null | undefined, upper: number | null | undefined): string {
  if (lower === null || lower === undefined || upper === null || upper === undefined) return "Not available";
  if (!Number.isFinite(lower) || !Number.isFinite(upper)) return "Not available";
  return `${formatNumber(lower)} to ${formatNumber(upper)}`;
}

export function formatConfidence(value: number): string {
  return `${formatNumber(value * 100, 1)}%`;
}

export function formatI2(value: number): string {
  return `${formatNumber(value * 100, 1)}%`;
}

export function formatTimestamp(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(date);
}

export function shortId(value: string): string {
  return `${value.slice(0, 8)}...`;
}

export function rejectionReasonLabel(value: number): string {
  return rejectionReasonLabels[value] ?? `Reason code ${value}`;
}

export function artifactLabel(result: QuantitativeResult): string {
  const outcome = result.outcomeGroupKey || "Unnamed outcome";
  const population = result.populationCompatibilityKey || "Population not specified";
  return `${outcome} - ${population}`;
}
