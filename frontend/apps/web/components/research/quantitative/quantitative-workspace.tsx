"use client";

import Link from "next/link";
import { ArrowLeft, Copy, Check, Printer, ShieldCheck } from "lucide-react";
import { useState } from "react";
import { MedResearchApiError } from "@medresearch/api";
import { Badge, Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@medresearch/ui";
import { useResearchQuantitative } from "../../../lib/api";
import { ErrorPanel, LoadingPanel } from "../../state-panel";
import { QuantitativeForestPlot } from "./forest-plot";
import { artifactLabel, effectMeasureLabel, formatConfidence, formatI2, formatInterval, formatNumber, formatTimestamp, methodLabel, rejectionReasonLabel, shortId, statusLabel, type QuantitativeArtifact, type QuantitativeContribution, type QuantitativeResult } from "./quantitative-format";

export function QuantitativeWorkspace({ researchRunId }: { researchRunId: string }) {
  const query = useResearchQuantitative(researchRunId);
  const [selectedArtifactId, setSelectedArtifactId] = useState<string | null>(null);

  if (query.isLoading) return <LoadingPanel title="Loading quantitative results" />;
  if (query.isError || !query.data) return <QuantitativeError error={query.error} />;
  if (query.data.length === 0) {
    return <div className="space-y-5"><BackLink researchRunId={researchRunId} /><Card><CardHeader><CardTitle>No quantitative artifact</CardTitle><CardDescription>This run has no persisted estimable quantitative synthesis. The workspace does not infer one from the narrative report.</CardDescription></CardHeader></Card></div>;
  }

  const artifact = query.data.find((item) => item.artifactId === selectedArtifactId) ?? query.data[0];
  return <QuantitativeArtifactView artifact={artifact} artifacts={query.data} researchRunId={researchRunId} onSelect={setSelectedArtifactId} />;
}

function QuantitativeArtifactView({ artifact, artifacts, researchRunId, onSelect }: { artifact: QuantitativeArtifact; artifacts: QuantitativeArtifact[]; researchRunId: string; onSelect: (id: string) => void }) {
  const result = artifact.result;
  return (
    <div className="quantitative-workspace space-y-6">
      <div className="quantitative-navigation flex flex-wrap items-center justify-between gap-3">
        <BackLink researchRunId={researchRunId} />
        <Button variant="secondary" size="sm" onClick={() => window.print()}><Printer className="h-4 w-4" />Print quantitative results</Button>
      </div>

      <header className="flex flex-col gap-4 border-b border-border pb-5 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <p className="text-xs font-medium uppercase text-muted-foreground">Quantitative results</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-normal">Scientific synthesis workspace</h1>
          <p className="mt-2 max-w-3xl text-sm text-muted-foreground">Persisted deterministic estimates and their Evidence lineage for this research run.</p>
        </div>
        <div className="flex flex-col items-start gap-2 lg:items-end"><Badge tone={result.status === 1 ? "success" : "warning"}>{statusLabel(result.status)}</Badge><span className="text-xs text-muted-foreground">Persisted {formatTimestamp(artifact.persistedAt)}</span></div>
      </header>

      {artifacts.length > 1 ? <label className="grid max-w-xl gap-1 text-sm"><span className="font-medium">Analysis group</span><select className="h-10 rounded-md border border-border bg-surface px-3 outline-none focus-visible:ring-2 focus-visible:ring-ring" value={artifact.artifactId} onChange={(event) => onSelect(event.target.value)}>{artifacts.map((item) => <option key={item.artifactId} value={item.artifactId}>{artifactLabel(item.result)}</option>)}</select></label> : null}

      <AnalysisHeader result={result} />
      <SummaryEffects result={result} />
      <section className="space-y-3"><SectionHeading title="Forest plot" description="Contribution point estimates and persisted pooled intervals on the analysis scale." /><Card><CardContent className="pt-6"><QuantitativeForestPlot result={result} /></CardContent></Card></section>
      <Heterogeneity result={result} />
      <Inference result={result} />
      <Prediction result={result} />
      <Contributions result={result} />
      <Reproducibility artifact={artifact} />
    </div>
  );
}

function AnalysisHeader({ result }: { result: QuantitativeResult }) {
  return <Card><CardContent className="grid gap-4 pt-6 sm:grid-cols-2 lg:grid-cols-4"><Meta label="Effect measure" value={effectMeasureLabel(result.effectMeasureType)} /><Meta label="Contributions" value={`${result.evidenceCount} evidence findings`} /><Meta label="Studies" value={String(result.uniqueStudyCount)} /><Meta label="Confidence level" value={formatConfidence(result.outputConfidenceLevel)} /><Meta label="Outcome group" value={result.outcomeGroupKey || "Not specified"} /><Meta label="Population" value={result.populationCompatibilityKey || "Not specified"} /><Meta label="Comparator" value={result.comparatorCompatibilityKey || "Not specified"} /><Meta label="Study design" value={result.studyDesignCompatibilityKey || "Not specified"} /></CardContent></Card>;
}

function SummaryEffects({ result }: { result: QuantitativeResult }) {
  return <section className="space-y-3"><SectionHeading title="Summary effects" description="Common-effect and random-effects outputs are shown as separate model results." /><div className="grid gap-4 lg:grid-cols-2"><SummaryCard title="Common effect" method={methodLabel(result.method)} effect={result.reportedScaleEffect} interval={formatInterval(result.reportedScaleConfidenceIntervalLower, result.reportedScaleConfidenceIntervalUpper)} analysisEffect={result.analysisScaleEffect} status={result.status} /><SummaryCard title="Random effects" method={result.randomEffects ? methodLabel(result.randomEffects.method) : "Not persisted"} effect={result.randomEffects?.reportedScaleEffect ?? null} interval={formatInterval(result.randomEffects?.reportedScaleConfidenceIntervalLower, result.randomEffects?.reportedScaleConfidenceIntervalUpper)} analysisEffect={result.randomEffects?.analysisScaleEffect ?? null} status={result.randomEffects?.status ?? 0} /></div></section>;
}

function SummaryCard({ title, method, effect, interval, analysisEffect, status }: { title: string; method: string; effect: number | null; interval: string; analysisEffect: number | null; status: number }) {
  return <Card><CardHeader><CardTitle className="text-base">{title}</CardTitle><CardDescription>{method}</CardDescription></CardHeader><CardContent className="space-y-4"><div><div className="text-xs uppercase text-muted-foreground">Reported-scale estimate</div><div className="mt-1 text-2xl font-semibold tabular-nums">{status === 1 ? formatNumber(effect) : "Not estimated"}</div></div><Meta label="Persisted interval" value={status === 1 ? interval : "Not estimated"} /><Meta label="Analysis-scale estimate" value={status === 1 ? formatNumber(analysisEffect) : "Not estimated"} /></CardContent></Card>;
}

function Heterogeneity({ result }: { result: QuantitativeResult }) {
  const diagnostics = result.heterogeneityDiagnostics;
  const tau = result.betweenStudyVariance;
  return <section className="space-y-3"><SectionHeading title="Heterogeneity" description="Persisted diagnostics; no qualitative heterogeneity classification is inferred." /><Card><CardContent className="grid gap-4 pt-6 sm:grid-cols-2 lg:grid-cols-4"><Meta label="Cochran's Q" value={diagnostics ? formatNumber(diagnostics.cochransQ) : "Not available"} /><Meta label="Degrees of freedom" value={diagnostics ? String(diagnostics.degreesOfFreedom) : "Not available"} /><Meta label="I²" value={diagnostics ? formatI2(diagnostics.iSquared) : "Not available"} /><Meta label="Tau²" value={tau?.tauSquared === null || tau?.tauSquared === undefined ? "Not estimated" : formatNumber(tau.tauSquared)} /><Meta label="Tau² status" value={tau ? (tau.status === 0 ? "Estimated" : "Not estimated") : "Not available"} /><Meta label="Tau² algorithm" value={tau?.algorithmVersion ?? "Not available"} /></CardContent></Card></section>;
}

function Inference({ result }: { result: QuantitativeResult }) {
  const random = result.randomEffects;
  const hksj = random?.hksjInference;
  return <section className="space-y-3"><SectionHeading title="Inference comparison" description="Wald and HKSJ intervals use the persisted random-effects point estimate; they are not separate pooled estimates." /><Card><CardContent className="space-y-5 pt-6">{random?.status === 1 ? <><div className="rounded-md bg-muted p-4"><div className="text-xs uppercase text-muted-foreground">Shared random-effects estimate</div><div className="mt-1 text-xl font-semibold tabular-nums">{formatNumber(random.reportedScaleEffect)}</div></div><div className="grid gap-4 md:grid-cols-2"><InferenceCard title="Wald interval" interval={formatInterval(random.reportedScaleConfidenceIntervalLower, random.reportedScaleConfidenceIntervalUpper)} method="Standard normal" /><InferenceCard title="HKSJ interval" interval={hksj?.status === 1 ? formatInterval(hksj.reportedScaleConfidenceIntervalLower, hksj.reportedScaleConfidenceIntervalUpper) : "Not estimated"} method={hksj?.status === 1 ? "Hartung-Knapp-Sidik-Jonkman" : "Not estimated"} /></div></> : <p className="text-sm text-muted-foreground">Random-effects inference is not estimated for this artifact.</p>}</CardContent></Card></section>;
}

function InferenceCard({ title, interval, method }: { title: string; interval: string; method: string }) {
  return <div className="rounded-md border border-border p-4"><h3 className="font-medium">{title}</h3><p className="mt-2 text-xl font-semibold tabular-nums">{interval}</p><p className="mt-1 text-xs text-muted-foreground">{method}</p></div>;
}

function Prediction({ result }: { result: QuantitativeResult }) {
  const prediction = result.randomEffects?.predictionInterval;
  return <section className="space-y-3"><SectionHeading title="Prediction interval" description="This describes the range implied for a new comparable study under the fitted random-effects model, not a future patient outcome." /><Card><CardContent className="grid gap-4 pt-6 sm:grid-cols-2 lg:grid-cols-4"><Meta label="Reported prediction interval" value={prediction?.status === 1 ? formatInterval(prediction.reportedScaleLower, prediction.reportedScaleUpper) : "Not estimated"} /><Meta label="Analysis-scale interval" value={prediction?.status === 1 ? formatInterval(prediction.analysisScaleLower, prediction.analysisScaleUpper) : "Not estimated"} /><Meta label="Degrees of freedom" value={prediction?.degreesOfFreedom === null || prediction?.degreesOfFreedom === undefined ? "Not available" : String(prediction.degreesOfFreedom)} /><Meta label="Method" value={prediction?.status === 1 ? `Student-t (${prediction.algorithmVersion})` : "Not estimated"} /></CardContent></Card></section>;
}

function Contributions({ result }: { result: QuantitativeResult }) {
  const randomByEvidence = new Map(result.randomEffects?.contributions.map((item) => [item.evidenceId, item]) ?? []);
  return <section className="space-y-3"><SectionHeading title="Contributions and lineage" description="These are the exact persisted contribution snapshots. Study-level confidence intervals are not available in F6." /><Card><CardContent className="overflow-x-auto pt-6"><table className="w-full min-w-[880px] text-left text-sm"><caption className="sr-only">Quantitative contribution snapshots and lineage identifiers</caption><thead><tr className="border-b border-border text-xs uppercase text-muted-foreground"><th className="px-3 py-3">Evidence</th><th className="px-3 py-3">Study</th><th className="px-3 py-3">Analysis effect</th><th className="px-3 py-3">SE</th><th className="px-3 py-3">Fixed normalized weight</th><th className="px-3 py-3">Random normalized weight</th><th className="px-3 py-3">Lineage</th></tr></thead><tbody>{result.contributions.map((contribution) => <ContributionRow key={contribution.evidenceId} contribution={contribution} random={randomByEvidence.get(contribution.evidenceId)} />)}</tbody></table></CardContent></Card></section>;
}

function ContributionRow({ contribution, random }: { contribution: QuantitativeContribution; random: QuantitativeContribution | undefined }) {
  return <tr className="border-b border-border align-top last:border-0"><td className="px-3 py-3 font-mono text-xs" title={contribution.evidenceId}>{shortId(contribution.evidenceId)}</td><td className="px-3 py-3 font-mono text-xs" title={contribution.studyId}>{shortId(contribution.studyId)}</td><td className="px-3 py-3 tabular-nums">{formatNumber(contribution.analysisScaleEffect)}</td><td className="px-3 py-3 tabular-nums">{formatNumber(contribution.analysisScaleStandardError)}</td><td className="px-3 py-3 tabular-nums">{formatNumber(contribution.normalizedWeight)}</td><td className="px-3 py-3 tabular-nums">{random ? formatNumber(random.normalizedWeight) : "Not available"}</td><td className="px-3 py-3"><details><summary className="cursor-pointer text-primary">View IDs</summary><dl className="mt-2 space-y-1 text-xs text-muted-foreground"><Meta label="Evidence" value={contribution.evidenceId} /><Meta label="Extraction" value={contribution.evidenceExtractionId} /><Meta label="Source material" value={contribution.sourceMaterialId} /><Meta label="Study" value={contribution.studyId} /></dl></details></td></tr>;
}

function Reproducibility({ artifact }: { artifact: QuantitativeArtifact }) {
  const [copied, setCopied] = useState(false);
  async function copyFingerprint() { await navigator.clipboard?.writeText(artifact.snapshotFingerprint); setCopied(true); window.setTimeout(() => setCopied(false), 1500); }
  const result = artifact.result;
  return <section className="space-y-3"><SectionHeading title="Reproducibility" description="Metadata identifies the immutable persisted result snapshot; it does not expose prompts or source content." /><Card><CardContent className="grid gap-4 pt-6 sm:grid-cols-2"><Meta label="Algorithm version" value={result.algorithmVersion} /><Meta label="Random-effects algorithm" value={result.randomEffects?.algorithmVersion ?? "Not available"} /><Meta label="Tau² algorithm" value={result.randomEffects?.tauSquaredAlgorithmVersion ?? result.betweenStudyVariance?.algorithmVersion ?? "Not available"} /><Meta label="Artifact ID" value={artifact.artifactId} /><div className="sm:col-span-2"><div className="text-xs text-muted-foreground">Snapshot fingerprint</div><div className="mt-1 flex flex-wrap items-center gap-2"><code className="break-all text-xs">{artifact.snapshotFingerprint}</code><Button type="button" variant="ghost" size="sm" onClick={copyFingerprint} title="Copy snapshot fingerprint">{copied ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}{copied ? "Copied" : "Copy"}</Button></div></div><div className="sm:col-span-2 flex items-start gap-2 text-xs text-muted-foreground"><ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-success" />Lineage identifiers point to the persisted Evidence, extraction, source-material, and Study records. Raw source material is intentionally not returned here.</div></CardContent></Card>{result.status !== 1 || result.rejectionReasons.length > 0 ? <Card><CardHeader><CardTitle className="text-base">Not-estimated details</CardTitle></CardHeader><CardContent className="space-y-2 text-sm">{result.rejectionReasons.length > 0 ? <ul className="list-disc space-y-1 pl-5">{result.rejectionReasons.map((reason) => <li key={reason}>{rejectionReasonLabel(reason)}</li>)}</ul> : <p className="text-muted-foreground">No rejection reason was persisted.</p>}</CardContent></Card> : null}</section>;
}

function SectionHeading({ title, description }: { title: string; description: string }) { return <div><h2 className="text-lg font-semibold tracking-normal">{title}</h2><p className="mt-1 text-sm text-muted-foreground">{description}</p></div>; }
function Meta({ label, value }: { label: string; value: string }) { return <div className="min-w-0"><dt className="text-xs text-muted-foreground">{label}</dt><dd className="mt-1 break-words font-medium">{value}</dd></div>; }
function BackLink({ researchRunId }: { researchRunId: string }) { return <Button asChild variant="ghost" size="sm"><Link href={`/research/${researchRunId}`}><ArrowLeft className="h-4 w-4" />Back to research run</Link></Button>; }
function QuantitativeError({ error }: { error: unknown }) { if (error instanceof MedResearchApiError && error.kind === "not-found") return <ErrorPanel title="Research run not found" message="The API could not find this research run." />; if (error instanceof MedResearchApiError && error.kind === "unauthorized") return <ErrorPanel title="Authentication required" message="Sign in to view quantitative results." />; return <ErrorPanel title="Quantitative results unavailable" message="The API could not return the persisted quantitative artifact." />; }
