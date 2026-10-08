"use client";

import Link from "next/link";
import { useState, type ReactNode } from "react";
import { ArrowLeft, ExternalLink, FileSearch, GitBranch, ShieldCheck } from "lucide-react";
import type { ResearchProvenanceResponse } from "@medresearch/api";
import { MedResearchApiError } from "@medresearch/api";
import { Badge, Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input } from "@medresearch/ui";
import { useResearchProvenance } from "../../lib/api";
import { ErrorPanel, LoadingPanel } from "../state-panel";

type Study = ResearchProvenanceResponse["studies"][number];
type Evidence = Study["evidence"][number];

export function EvidenceProvenanceWorkspace({ researchRunId }: { researchRunId: string }) {
  const query = useResearchProvenance(researchRunId);
  const [filter, setFilter] = useState("");

  if (query.isLoading) return <LoadingPanel title="Loading evidence provenance" />;
  if (query.isError || !query.data) {
    if (query.error instanceof MedResearchApiError && query.error.kind === "unauthorized") {
      return <ErrorPanel title="Authentication required" message="Sign in to view this research provenance." />;
    }
    return <ErrorPanel title="Provenance unavailable" message="The API could not return persisted evidence lineage for this research run." />;
  }

  const provenance = query.data;
  const normalizedFilter = filter.trim().toLowerCase();
  const studies = provenance.studies.filter(study => matchesStudy(study, normalizedFilter));

  return (
    <div className="space-y-6">
      <header className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <Button asChild variant="ghost" size="sm">
            <Link href={`/research/${researchRunId}`}><ArrowLeft className="h-4 w-4" /> Research run</Link>
          </Button>
          <div className="flex flex-wrap gap-2">
            <Button asChild variant="secondary" size="sm"><Link href={`/research/${researchRunId}/report`}>Report</Link></Button>
            <Button asChild variant="secondary" size="sm"><Link href={`/research/${researchRunId}/quantitative`}>Quantitative</Link></Button>
          </div>
        </div>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <h1 className="text-2xl font-semibold">Evidence &amp; Provenance</h1>
            <p className="mt-2 max-w-4xl text-sm text-muted-foreground">{provenance.question}</p>
          </div>
          <Badge>{provenance.status}</Badge>
        </div>
        <p className="max-w-4xl text-sm text-muted-foreground">
          This read-only workspace shows persisted lineage for this run. It does not reconstruct citations, expose raw source bodies, or infer missing scientific data.
        </p>
      </header>

      <Coverage coverage={provenance.coverage} />
      <ProviderAttempts attempts={provenance.providerAttempts} />
      <SearchExecutions provenance={provenance} />

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2"><FileSearch className="h-4 w-4" /> Studies and evidence lineage</CardTitle>
          <CardDescription>Each canonical Study can have several search discovery paths, while extraction and Evidence remain scoped to this ResearchRun.</CardDescription>
          <Input aria-label="Filter studies" placeholder="Filter by title, identifier, journal, or source" value={filter} onChange={event => setFilter(event.target.value)} />
        </CardHeader>
        <CardContent className="space-y-4">
          {studies.length === 0 ? (
            <p className="rounded-md border border-dashed border-border p-4 text-sm text-muted-foreground">No persisted studies match this filter.</p>
          ) : (
            studies.map(study => <StudyLineage key={study.studyId} researchRunId={researchRunId} study={study} contributions={provenance.quantitativeContributions} />)
          )}
        </CardContent>
      </Card>

      <ClaimsSection claims={provenance.reportClaims} />
    </div>
  );
}

function ProviderAttempts({ attempts }: { attempts: ResearchProvenanceResponse["providerAttempts"] }) {
  const labels = { Started: "Started / outcome not recorded", SucceededWithResults: "Succeeded", SucceededZeroResults: "No results", Failed: "Failed", TimedOut: "Timed out", Cancelled: "Cancelled" };
  return <section className="space-y-3" aria-label="Provider coverage">
    <h2 className="text-lg font-semibold">Provider coverage</h2>
    {attempts.length === 0 ? <p className="text-sm text-muted-foreground">No provider attempts recorded. Historical searches may predate attempt tracking.</p> :
      <div className="divide-y divide-border border-y border-border">{attempts.map(attempt => <div key={attempt.attemptId} className="grid gap-2 py-3 text-sm md:grid-cols-[100px_1fr_auto]">
        <span className="font-medium">{attempt.source}</span>
        <div className="min-w-0"><p className="break-words">{attempt.query}</p><p className="mt-1 text-xs text-muted-foreground">{formatTimestamp(attempt.startedAt)}{attempt.completedAt ? ` - ${formatTimestamp(attempt.completedAt)}` : ""}</p></div>
        <div><Badge tone={attempt.failureCategory ? "neutral" : undefined}>{labels[attempt.status]}</Badge>{attempt.resultCount !== null ? <p className="mt-1 text-xs">{attempt.resultCount} provider result(s)</p> : null}{attempt.failureCategory ? <p className="mt-1 text-xs text-muted-foreground">Reason: {attempt.failureCategory}</p> : null}</div>
      </div>)}</div>}
  </section>;
}

function Coverage({ coverage }: { coverage: ResearchProvenanceResponse["coverage"] }) {
  const metrics = [
    ["Searches", coverage.literatureSearchCount], ["Discovery paths", coverage.discoveryPathCount], ["Distinct studies", coverage.distinctStudyCount],
    ["Source materials", coverage.sourceMaterialCount], ["Extractions", coverage.evidenceExtractionCount], ["Evidence", coverage.evidenceFindingCount],
    ["Evaluations", coverage.evidenceEvaluationCount], ["Report claims", coverage.researchReportClaimCount]
  ] as const;
  return <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">{metrics.map(([label, value]) => <div key={label} className="rounded-md border border-border bg-card p-3"><div className="text-xs text-muted-foreground">{label}</div><div className="mt-1 text-xl font-semibold tabular-nums">{value}</div></div>)}</div>;
}

function SearchExecutions({ provenance }: { provenance: ResearchProvenanceResponse }) {
  return <Card><CardHeader><CardTitle>Search executions</CardTitle><CardDescription>One planned query may produce one persisted execution per enabled provider.</CardDescription></CardHeader><CardContent className="space-y-2">{provenance.searches.length === 0 ? <p className="text-sm text-muted-foreground">No search execution is persisted yet.</p> : provenance.searches.map(search => <div key={search.literatureSearchId} className="grid gap-2 rounded-md border border-border p-3 text-sm md:grid-cols-[100px_1fr_auto] md:items-start"><Badge>{search.source}</Badge><div><div className="font-medium break-words">{search.query}</div><div className="mt-1 text-xs text-muted-foreground">{search.resultStatus} · {search.resultCount} provider result(s) · {search.persistedStudyCount} persisted study path(s) · {search.duplicateStudyCount} duplicate(s)</div></div><div className="text-xs text-muted-foreground">{formatTimestamp(search.searchedAt)}</div></div>)}</CardContent></Card>;
}

function StudyLineage({ researchRunId, study, contributions }: { researchRunId: string; study: Study; contributions: ResearchProvenanceResponse["quantitativeContributions"] }) {
  const studyContributions = contributions.filter(item => item.studyId === study.studyId);
  return <article className="rounded-md border border-border p-4" id={`study-${study.studyId}`}>
    <div className="flex flex-col gap-2 lg:flex-row lg:items-start lg:justify-between"><div><h3 className="font-semibold">{study.title}</h3><p className="mt-1 text-xs text-muted-foreground">{study.journal ?? "Journal not available"}{study.publicationYear ? ` · ${study.publicationYear}` : ""} · {study.source}</p></div><div className="flex flex-wrap gap-2">{study.pmid ? <Identifier label="PMID" value={study.pmid} href={`https://pubmed.ncbi.nlm.nih.gov/${encodeURIComponent(study.pmid)}/`} /> : null}{study.pmcid ? <Identifier label="PMCID" value={study.pmcid} href={`https://pmc.ncbi.nlm.nih.gov/articles/${encodeURIComponent(study.pmcid)}/`} /> : null}{study.doi ? <Identifier label="DOI" value={study.doi} href={`https://doi.org/${encodeURIComponent(study.doi)}`} /> : null}</div></div>
    <div className="mt-4 grid gap-4 xl:grid-cols-2">
      <LineageSection title={`Discovery paths (${study.discoveryPaths.length})`} icon={<GitBranch className="h-4 w-4" />}>
        {study.discoveryPaths.map(path => <div key={path.researchStudyDiscoveryId} className="border-l-2 border-primary/30 pl-3 text-xs"><div className="font-medium">{path.source} · {path.sourceStudyIdentifier ?? "Provider ID not available"}</div><div className="mt-1 text-muted-foreground">Query: {path.query}</div></div>)}
      </LineageSection>
      <LineageSection title={`Source material metadata (${study.sourceMaterials.length})`} icon={<ShieldCheck className="h-4 w-4" />}>
        {study.sourceMaterials.length === 0 ? <EmptyLine text="No source material metadata is persisted." /> : study.sourceMaterials.map(material => <div key={material.sourceMaterialId} className="border-l-2 border-border pl-3 text-xs"><div className="font-medium">{material.type} · {material.provider} · version {material.contentVersion}{material.isCurrent ? " · current" : ""}</div><div className="mt-1 text-muted-foreground">{material.retrievalMethod} · {material.characterCount} characters · {material.sectionNames.join(", ") || "Sections not available"}</div><div className="mt-1 text-muted-foreground">Content is intentionally not displayed. Hash: {material.contentHash}</div></div>)}
      </LineageSection>
    </div>
    <div className="mt-4 space-y-3"><h4 className="text-sm font-medium">Extraction, Evidence, and evaluation</h4>{study.extractions.map(extraction => <div key={extraction.evidenceExtractionId} className="rounded-md bg-muted/40 p-3 text-sm"><div className="flex flex-wrap justify-between gap-2"><span className="font-medium">Extraction {extraction.status}</span><span className="text-xs text-muted-foreground">{extraction.evidenceCount} finding(s) · {extraction.groundingValidated ? "grounding validated" : "grounding not validated"}</span></div>{study.evidence.filter(item => item.evidenceExtractionId === extraction.evidenceExtractionId).length > 0 ? <div className="mt-3 space-y-2">{study.evidence.filter(item => item.evidenceExtractionId === extraction.evidenceExtractionId).map(item => <EvidenceItem key={item.evidenceId} evidence={item} />)}</div> : <p className="mt-2 text-xs text-muted-foreground">No validated Evidence is available for this extraction.</p>}</div>)}{study.extractions.length === 0 ? <EmptyLine text="No extraction is persisted for this study in this run." /> : null}{study.evaluations.map(evaluation => <div key={evaluation.evidenceEvaluationId} className="rounded-md border border-border p-3 text-xs"><div className="font-medium">Evaluation: {evaluation.status} · {evaluation.overallConfidence}</div><p className="mt-1 text-muted-foreground">{evaluation.rationale}</p><div className="mt-2 text-muted-foreground">Evidence IDs: {evaluation.evidenceIds.length || "none"}</div></div>)}</div>
    {studyContributions.length > 0 ? <div className="mt-4 rounded-md border border-primary/30 bg-primary/5 p-3 text-xs"><div className="font-medium">Quantitative contribution lineage</div>{studyContributions.map(item => <Link key={`${item.artifactId}-${item.ordinal}`} className="mt-1 block text-primary underline-offset-4 hover:underline" href={`/research/${researchRunId}/evidence#evidence-${item.evidenceId}`}>{item.groupKey} · {item.analysisMethod} · Evidence {item.evidenceId}</Link>)}</div> : null}
  </article>;
}

function EvidenceItem({ evidence }: { evidence: Evidence }) {
  return <div id={`evidence-${evidence.evidenceId}`} className="rounded-md border border-border bg-background p-3"><div className="flex flex-wrap items-center justify-between gap-2"><a className="font-medium text-primary underline-offset-4 hover:underline" href={`#evidence-${evidence.evidenceId}`}>Evidence {evidence.direction}</a><Badge>{evidence.groundingValidated ? "Grounded" : "Not validated"}</Badge></div><div className="mt-2 font-medium">{evidence.outcome}</div><p className="mt-1 text-sm">{evidence.resultSummary}</p><p className="mt-2 border-l-2 border-primary/30 pl-3 text-xs text-muted-foreground">{evidence.supportingText}</p></div>;
}

function ClaimsSection({ claims }: { claims: ResearchProvenanceResponse["reportClaims"] }) {
  return <Card><CardHeader><CardTitle>Report claim lineage</CardTitle><CardDescription>Claims link to persisted Evidence IDs; the UI does not invent citations or identifiers.</CardDescription></CardHeader><CardContent className="space-y-3">{claims.length === 0 ? <p className="text-sm text-muted-foreground">No report claims are persisted for this run.</p> : claims.map(claim => <div key={claim.researchReportClaimId} className="rounded-md border border-border p-3"><div className="flex flex-wrap gap-2"><Badge>{claim.claimType}</Badge><Badge tone="neutral">{claim.direction}</Badge><Badge tone={claim.groundingStatus === "StructuredValidated" ? "info" : "warning"}>{claim.groundingStatus === "StructuredValidated" ? "Structured validated" : "Legacy claim unverified"}</Badge></div><p className="mt-2 text-sm">{claim.text}</p><div className="mt-2 flex flex-wrap gap-2">{claim.evidenceIds.length > 0 ? claim.evidenceIds.map(id => <a key={id} className="text-xs text-primary underline-offset-4 hover:underline" href={`#evidence-${id}`}>Evidence {id}</a>) : <span className="text-xs text-muted-foreground">No linked Evidence</span>}</div></div>)}</CardContent></Card>;
}

function LineageSection({ title, icon, children }: { title: string; icon: ReactNode; children: ReactNode }) {
  return <section className="space-y-3"><h4 className="flex items-center gap-2 text-sm font-medium">{icon}{title}</h4><div className="space-y-2">{children}</div></section>;
}

function Identifier({ label, value, href }: { label: string; value: string; href: string }) {
  return <a className="inline-flex items-center gap-1 text-xs text-primary underline-offset-4 hover:underline" href={href} target="_blank" rel="noreferrer">{label}: {value}<ExternalLink className="h-3 w-3" /></a>;
}

function EmptyLine({ text }: { text: string }) { return <p className="text-xs text-muted-foreground">{text}</p>; }

function matchesStudy(study: Study, filter: string) {
  if (!filter) return true;
  return [study.title, study.journal, study.pmid, study.pmcid, study.doi, study.source, ...study.authors].some(value => value?.toLowerCase().includes(filter));
}

function formatTimestamp(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
