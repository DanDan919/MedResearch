import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { QuantitativeSynthesisArtifactResponse } from "@medresearch/api";
import { QuantitativeWorkspace } from "../components/research/quantitative/quantitative-workspace";

const fetchMock = vi.fn<typeof fetch>();
const runId = "11111111-1111-4111-8111-111111111111";

describe("QuantitativeWorkspace", () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
  });

  it("presents persisted summaries, inference, prediction, and lineage without deriving study intervals", async () => {
    fetchMock.mockResolvedValue(jsonResponse([artifact()]));

    renderWithClient(<QuantitativeWorkspace researchRunId={runId} />);

    expect(await screen.findByRole("heading", { name: "Scientific synthesis workspace" })).toBeInTheDocument();
    expect(screen.getByText("Odds ratio")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Common effect" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Random effects" })).toBeInTheDocument();
    expect(screen.getByText("Wald interval")).toBeInTheDocument();
    expect(screen.getByText("HKSJ interval")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Prediction interval" })).toBeInTheDocument();
    expect(screen.getByText(/Study-level confidence intervals are not available/)).toBeInTheDocument();
    expect(screen.getAllByText("View IDs")).toHaveLength(2);
    expect(screen.getByText(/new comparable study/)).toBeInTheDocument();
  });

  it("keeps tau-squared zero distinct from an unavailable estimate and supports multiple groups", async () => {
    fetchMock.mockResolvedValue(jsonResponse([artifact(), artifact({ artifactId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", tauSquared: null, outcome: "secondary outcome" })]));

    renderWithClient(<QuantitativeWorkspace researchRunId={runId} />);

    const selector = await screen.findByRole("combobox", { name: "Analysis group" });
    expect(screen.getByText("0")).toBeInTheDocument();
    expect(screen.getByText("Estimated")).toBeInTheDocument();

    fireEvent.change(selector, { target: { value: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa" } });

    expect(screen.getByText("secondary outcome")).toBeInTheDocument();
  });

  it("shows an honest empty state when a run has no quantitative artifact", async () => {
    fetchMock.mockResolvedValue(jsonResponse([]));

    renderWithClient(<QuantitativeWorkspace researchRunId={runId} />);

    expect(await screen.findByText("No quantitative artifact")).toBeInTheDocument();
    expect(screen.queryByText("Scientific synthesis workspace")).not.toBeInTheDocument();
  });

  it("renders the accessible plot description and handles a larger contribution set", async () => {
    fetchMock.mockResolvedValue(jsonResponse([artifact({ contributionCount: 60 })]));

    renderWithClient(<QuantitativeWorkspace researchRunId={runId} />);

    expect(await screen.findByRole("img", { name: "Quantitative contribution plot" })).toBeInTheDocument();
    expect(screen.getAllByText(/View IDs/)).toHaveLength(60);
  });
});

function renderWithClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
}

function artifact(options: { artifactId?: string; tauSquared?: number | null; outcome?: string; contributionCount?: number } = {}): QuantitativeSynthesisArtifactResponse {
  const contributionCount = options.contributionCount ?? 2;
  const contributions = Array.from({ length: contributionCount }, (_, index) => contribution(index));
  const result = {
    researchRunId: runId,
    groupKey: `${options.outcome ?? "memory"}|adult|control|trial`,
    outcomeGroupKey: options.outcome ?? "memory",
    populationCompatibilityKey: "adult",
    comparatorCompatibilityKey: "control",
    studyDesignCompatibilityKey: "trial",
    effectMeasureType: 1,
    status: 1,
    method: 0,
    algorithmVersion: "fixed-effect-inverse-variance-v1",
    outputConfidenceLevel: 0.95,
    evidenceCount: contributionCount,
    uniqueStudyCount: contributionCount,
    analysisScaleEffect: 0.25,
    analysisScaleVariance: 0.04,
    analysisScaleStandardError: 0.2,
    analysisScaleConfidenceIntervalLower: -0.14,
    analysisScaleConfidenceIntervalUpper: 0.64,
    reportedScaleEffect: 1.28,
    reportedScaleConfidenceIntervalLower: 0.87,
    reportedScaleConfidenceIntervalUpper: 1.9,
    heterogeneityDiagnostics: { cochransQ: 3.2, degreesOfFreedom: Math.max(1, contributionCount - 1), iSquared: 0.6875, studyCount: contributionCount, algorithmVersion: "cochran-q-i2-v1" },
    betweenStudyVariance: { tauSquared: options.tauSquared ?? 0, estimator: 0, status: 0, algorithmVersion: "reml-tau-squared-v1", studyCount: contributionCount, converged: true, iterationCount: 4, failureReason: null },
    randomEffects: {
      status: 1,
      method: 1,
      algorithmVersion: "random-effects-inverse-variance-v1",
      confidenceIntervalMethod: 0,
      outputConfidenceLevel: 0.95,
      tauSquared: options.tauSquared ?? 0,
      tauSquaredEstimator: 0,
      tauSquaredAlgorithmVersion: "reml-tau-squared-v1",
      studyCount: contributionCount,
      analysisScaleEffect: 0.27,
      analysisScaleVariance: 0.05,
      analysisScaleStandardError: 0.22,
      analysisScaleConfidenceIntervalLower: -0.16,
      analysisScaleConfidenceIntervalUpper: 0.7,
      reportedScaleEffect: 1.31,
      reportedScaleConfidenceIntervalLower: 0.85,
      reportedScaleConfidenceIntervalUpper: 2.0,
      hksjInference: { status: 1, confidenceIntervalMethod: 1, algorithmVersion: "hksj-v1", outputConfidenceLevel: 0.95, studyCount: contributionCount, degreesOfFreedom: Math.max(1, contributionCount - 1), varianceAdjustment: 1.1, criticalValue: 2.2, analysisScaleEffect: 0.27, analysisScaleVariance: 0.055, analysisScaleStandardError: 0.2345, analysisScaleConfidenceIntervalLower: -0.25, analysisScaleConfidenceIntervalUpper: 0.79, reportedScaleEffect: 1.31, reportedScaleConfidenceIntervalLower: 0.78, reportedScaleConfidenceIntervalUpper: 2.2, failureReasons: [] },
      predictionInterval: { status: 1, method: 0, algorithmVersion: "prediction-v1", outputConfidenceLevel: 0.95, studyCount: contributionCount, degreesOfFreedom: Math.max(1, contributionCount - 1), tauSquared: options.tauSquared ?? 0, summaryEffectVariance: 0.05, summaryEffectStandardError: 0.22, predictionVariance: 0.06, predictionStandardError: 0.245, criticalValue: 2.2, analysisScaleEffect: 0.27, analysisScaleLower: -0.4, analysisScaleUpper: 0.94, reportedScaleEffect: 1.31, reportedScaleLower: 0.67, reportedScaleUpper: 2.56, failureReasons: [] },
      contributions,
      failureReasons: []
    },
    contributions,
    rejectionReasons: []
  };

  return { artifactId: options.artifactId ?? "22222222-2222-4222-8222-222222222222", persistedAt: "2026-09-29T12:05:00Z", snapshotFingerprint: "a".repeat(64), result } as QuantitativeSynthesisArtifactResponse;
}

function contribution(index: number) {
  const suffix = (index + 1).toString(16).padStart(12, "0");
  const prefix = suffix.slice(0, 8);
  return { evidenceId: `${prefix}-0000-4000-8000-${suffix}`, studyId: `${prefix}-1111-4111-8111-${suffix}`, evidenceExtractionId: `${prefix}-2222-4222-8222-${suffix}`, sourceMaterialId: `${prefix}-3333-4333-8333-${suffix}`, analysisScaleEffect: 0.1 + index / 100, analysisScaleVariance: 0.1, analysisScaleStandardError: 0.316, weight: 10, normalizedWeight: 1 / 2 };
}
