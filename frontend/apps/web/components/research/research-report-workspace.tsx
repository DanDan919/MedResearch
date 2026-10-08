"use client";

import Link from "next/link";
import { ArrowLeft, ExternalLink, FileText, Printer, ShieldCheck } from "lucide-react";
import { MedResearchApiError } from "@medresearch/api";
import type { ResearchReportResponse } from "@medresearch/api";
import { Badge, Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@medresearch/ui";
import { useResearchQuantitative, useResearchReport } from "../../lib/api";
import { ErrorPanel, LoadingPanel } from "../state-panel";

type Citation = ResearchReportResponse["claims"][number]["citations"][number];

export function ResearchReportWorkspace({ researchRunId }: { researchRunId: string }) {
  const query = useResearchReport(researchRunId, true);
  const quantitativeQuery = useResearchQuantitative(researchRunId, query.isSuccess);

  if (query.isLoading) return <LoadingPanel title="Loading report" />;
  if (query.isError || !query.data) return <ReportError error={query.error} />;

  const report = query.data;

  return (
    <div className="report-workspace space-y-6">
      <div className="report-navigation flex flex-wrap items-center justify-between gap-3">
        <Button asChild variant="ghost" size="sm">
          <Link href={`/research/${researchRunId}`}><ArrowLeft className="h-4 w-4" />Back to research run</Link>
        </Button>
        <Button variant="secondary" size="sm" onClick={() => window.print()}>
          <Printer className="h-4 w-4" />Print report
        </Button>
      </div>

      <header className="flex flex-col gap-4 border-b border-border pb-5 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <p className="text-xs font-medium uppercase tracking-normal text-muted-foreground">Scientific report</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-normal">Research Report</h1>
          <p className="mt-2 max-w-3xl text-sm text-muted-foreground">{report.question}</p>
        </div>
        <div className="flex flex-col items-start gap-2 sm:items-end">
          <Badge tone={report.status === "Completed" ? "success" : "warning"}>{report.status}</Badge>
          <Badge tone={report.narrativeAuthority === "StructuredClaims" ? "info" : "warning"}>{report.narrativeAuthority === "StructuredClaims" ? "Structured claims" : "Legacy narrative unverified"}</Badge>
          <p className="text-xs text-muted-foreground">Generated {formatTimestamp(report.generatedAt)}</p>
          {quantitativeQuery.data && quantitativeQuery.data.length > 0 ? <Button asChild variant="secondary" size="sm"><Link href={`/research/${researchRunId}/quantitative`}>Quantitative results</Link></Button> : null}
        </div>
      </header>

      <CoveragePanel report={report} />

      <Card>
        <CardHeader><CardTitle>Report summary</CardTitle><CardDescription>Persisted synthesis narrative and deterministic limitations.</CardDescription></CardHeader>
        <CardContent className="space-y-5 text-sm">
          <ReportSection title="Executive summary" value={report.executiveSummary} />
          <ReportSection title="Evidence summary" value={report.evidenceSummary} />
          <ReportSection title="Conflict summary" value={report.conflictSummary} />
          <ReportSection title="Limitations" value={report.limitationsSummary} />
          <ReportSection title="Conclusion" value={report.conclusion} />
          {report.insufficientEvidenceReason ? <ReportSection title="Evidence status" value={report.insufficientEvidenceReason} /> : null}
          {report.deterministicLimitations.length > 0 ? (
            <section>
              <h2 className="text-sm font-medium">Deterministic limitations</h2>
              <ul className="mt-2 list-disc space-y-1 pl-5 text-muted-foreground">
                {report.deterministicLimitations.map((limitation) => <li key={limitation}>{limitation}</li>)}
              </ul>
            </section>
          ) : null}
        </CardContent>
      </Card>

      <section aria-labelledby="claims-heading" className="space-y-4">
        <div>
          <h2 id="claims-heading" className="text-lg font-semibold tracking-normal">Claims and evidence</h2>
          <p className="mt-1 text-sm text-muted-foreground">Each claim is linked to the persisted evidence and publication metadata used by the report.</p>
        </div>
        {report.claims.length === 0 ? (
          <Card><CardContent className="py-6 text-sm text-muted-foreground">No report claims were persisted.</CardContent></Card>
        ) : (
          <div className="space-y-4">
            {report.claims.map((claim) => (
              <Card key={claim.claimId}>
                <CardHeader>
                  <div className="flex flex-wrap items-center justify-between gap-2"><CardTitle className="text-base">{claim.claimType}</CardTitle><Badge tone="info">{claim.direction}</Badge></div>
                  <CardDescription>Claim {claim.ordinal + 1} · {claim.citations.length} cited {claim.citations.length === 1 ? "finding" : "findings"}</CardDescription>
                  <Badge tone={claim.groundingStatus === "StructuredValidated" ? "info" : "warning"}>{claim.groundingStatus === "StructuredValidated" ? "Structured validated" : "Legacy claim unverified"}</Badge>
                </CardHeader>
                <CardContent className="space-y-4 text-sm">
                  <p className="leading-6">{claim.text}</p>
                  {claim.semantics ? <details className="border-t border-border pt-3">
                    <summary className="cursor-pointer font-medium">Claim support</summary>
                    <dl className="mt-3 grid gap-3 text-xs sm:grid-cols-2">
                      <MetadataField label="Kind" value={claim.semantics.kind} />
                      <MetadataField label="Outcome" value={claim.semantics.outcome} />
                      <MetadataField label="Population" value={claim.semantics.population} />
                      <MetadataField label="Intervention / exposure" value={claim.semantics.exposureOrIntervention} />
                      <MetadataField label="Comparator" value={claim.semantics.comparator} />
                      <MetadataField label="Timepoint" value={claim.semantics.timepoint} />
                      <MetadataField label="Statistic" value={claim.semantics.statistic} />
                      <MetadataField label="Artifact fingerprint" value={claim.semantics.snapshotFingerprint} />
                    </dl>
                    <Link className="mt-3 block break-all text-primary underline" href={`/research/${researchRunId}/evidence`}>Evidence and source provenance</Link>
                    {claim.semantics.quantitativeArtifactId ? <Link className="mt-2 block break-all text-primary underline" href={`/research/${researchRunId}/quantitative`}>Quantitative artifact: {claim.semantics.quantitativeArtifactId}</Link> : null}
                    <p className="mt-2 break-all text-xs text-muted-foreground">Evidence IDs: {claim.semantics.evidenceIds.join(", ") || "None"}</p>
                  </details> : null}
                  {claim.citations.map((citation) => <EvidenceDisclosure key={`${claim.claimId}-${citation.evidenceId}`} citation={citation} />)}
                </CardContent>
              </Card>
            ))}
          </div>
        )}
      </section>

      <div className="report-navigation border-t border-border pt-4">
        <Button asChild variant="ghost" size="sm"><Link href={`/research/${researchRunId}`}><ArrowLeft className="h-4 w-4" />Back to research run</Link></Button>
      </div>
    </div>
  );
}

function ReportError({ error }: { error: unknown }) {
  if (error instanceof MedResearchApiError && error.kind === "conflict") {
    return <Card className="border-warning/50"><CardContent className="flex items-start gap-3 py-6"><FileText className="mt-0.5 h-4 w-4 text-warning" /><div><div className="text-sm font-medium">Report not ready</div><p className="mt-1 text-sm text-muted-foreground">This research run is known, but its persisted report is not available yet. Check back after synthesis completes.</p></div></CardContent></Card>;
  }
  if (error instanceof MedResearchApiError && error.kind === "not-found") return <ErrorPanel title="Research run not found" message="The API could not find this research run." />;
  if (error instanceof MedResearchApiError && error.kind === "unauthorized") return <ErrorPanel title="Authentication required" message="Sign in to view this research report." />;
  return <ErrorPanel title="Report unavailable" message="The API could not return a report for this research run." />;
}

function CoveragePanel({ report }: { report: ResearchReportResponse }) {
  const coverage = report.coverage;
  const flags = [coverage.usesAbstractLevelEvidenceOnly ? "Abstract-level evidence only" : null, coverage.evidenceTruncated ? "Evidence was truncated" : null, coverage.potentialConflictDetected ? "Potential conflict detected" : null].filter((flag): flag is string => flag !== null);
  return <Card>
    <CardHeader><CardTitle>Evidence coverage</CardTitle><CardDescription>{coverage.includedEvidenceFindingCount} included findings across {coverage.includedStudyCount} studies.</CardDescription></CardHeader>
    <CardContent className="space-y-4 text-sm">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <CoverageMetric label="Discovered studies" value={coverage.discoveredStudyCount} /><CoverageMetric label="Extracted studies" value={coverage.extractedStudyCount} /><CoverageMetric label="Evaluated studies" value={coverage.evaluatedStudyCount} /><CoverageMetric label="Search queries" value={coverage.searchQueryCount} />
      </div>
      <div className="flex flex-wrap items-center gap-2"><span className="text-muted-foreground">Sources searched:</span>{coverage.searchedSources.length > 0 ? coverage.searchedSources.map((source) => <Badge key={source}>{source}</Badge>) : <span className="text-muted-foreground">Not available</span>}</div>
      {flags.length > 0 ? <div className="flex flex-wrap gap-2" aria-label="Report caveats">{flags.map((flag) => <Badge key={flag} tone="warning">{flag}</Badge>)}</div> : null}
    </CardContent>
  </Card>;
}

function EvidenceDisclosure({ citation }: { citation: Citation }) {
  return <details className="group rounded-md border border-border bg-background">
    <summary className="flex cursor-pointer list-none items-start justify-between gap-3 p-4 [&::-webkit-details-marker]:hidden">
      <span className="min-w-0"><span className="block font-medium">{citation.title}</span><span className="mt-1 block text-xs text-muted-foreground">{citation.studySource ?? "Source not available"} · {citation.sourceScope} evidence · {citation.evidenceDirection}</span></span>
      <span className="shrink-0 text-xs text-primary group-open:hidden">View evidence</span><span className="hidden shrink-0 text-xs text-primary group-open:inline">Hide evidence</span>
    </summary>
    <div className="space-y-4 border-t border-border p-4"><StudyMetadata citation={citation} />{citation.resultSummary ? <ReportSection title="Unverified extracted summary" value={citation.resultSummary} /> : null}{citation.supportingText ? <ReportSection title="Extracted supporting text" value={citation.supportingText} /> : null}<EvidenceDetails citation={citation} />{citation.sourceMaterial ? <SourceMaterialMetadata citation={citation} /> : null}</div>
  </details>;
}

function StudyMetadata({ citation }: { citation: Citation }) {
  return <section><h3 className="flex items-center gap-2 text-sm font-medium"><ShieldCheck className="h-4 w-4 text-success" />Study metadata</h3><dl className="mt-3 grid gap-3 text-xs sm:grid-cols-2"><MetadataField label="Journal" value={citation.journal} /><MetadataField label="Publication date" value={formatPublicationDate(citation)} /><MetadataField label="Authors" value={citation.authors.length > 0 ? citation.authors.join(", ") : null} /><MetadataField label="Publication types" value={citation.publicationTypes.length > 0 ? citation.publicationTypes.join(", ") : null} /></dl><div className="mt-3 flex flex-wrap gap-x-4 gap-y-2 text-xs"><IdentifierLink label="PMID" value={citation.pmid} href={citation.pmid ? `https://pubmed.ncbi.nlm.nih.gov/${encodeURIComponent(citation.pmid)}/` : null} /><IdentifierLink label="PMCID" value={citation.pmcid} href={citation.pmcid ? `https://pmc.ncbi.nlm.nih.gov/articles/${encodeURIComponent(citation.pmcid)}/` : null} /><IdentifierLink label="DOI" value={citation.doi} href={citation.doi ? `https://doi.org/${encodeURIComponent(citation.doi)}` : null} /></div></section>;
}

function EvidenceDetails({ citation }: { citation: Citation }) {
  const fields = [["Outcome", citation.outcome], ["Population", citation.population], ["Exposure or intervention", citation.exposureOrIntervention], ["Comparator", citation.comparator], ["Study design", citation.studyDesign], ["Sample size", citation.sampleSize === null ? null : String(citation.sampleSize)], ["Effect measure", citation.effectMeasure], ["Effect value", citation.effectValue === null ? null : String(citation.effectValue)], ["Confidence interval", formatInterval(citation)], ["Confidence level", citation.confidenceLevel === null ? null : String(citation.confidenceLevel)], ["Reported standard error", citation.reportedStandardError === null ? null : String(citation.reportedStandardError)], ["P value", citation.pValue === null ? null : String(citation.pValue)]] as const;
  const available = fields.filter(([, value]) => value !== null && value !== "");
  return available.length > 0 ? <section><h3 className="text-sm font-medium">Evidence details</h3><dl className="mt-3 grid gap-3 text-xs sm:grid-cols-2">{available.map(([label, value]) => <div key={label}><dt className="text-muted-foreground">{label}</dt><dd className="mt-1 break-words font-medium">{value}</dd></div>)}</dl></section> : null;
}

function SourceMaterialMetadata({ citation }: { citation: Citation }) {
  const source = citation.sourceMaterial;
  if (!source) return null;
  return <section className="border-t border-border pt-3 text-xs text-muted-foreground"><h3 className="flex items-center gap-2 font-medium text-foreground"><FileText className="h-4 w-4" />Source material lineage</h3><p className="mt-1">{source.type} from {source.provider} via {source.retrievalMethod}. Retrieved {formatTimestamp(source.retrievedAt)}.{source.wasTruncated ? " Material was truncated." : ""}</p>{source.sectionNames.length > 0 ? <p className="mt-1">Sections: {source.sectionNames.join(", ")}</p> : null}</section>;
}

function MetadataField({ label, value }: { label: string; value: string | null }) {
  return <div><dt className="text-muted-foreground">{label}</dt><dd className="mt-1 break-words font-medium">{value ?? "Not available"}</dd></div>;
}

function IdentifierLink({ label, value, href }: { label: string; value: string | null; href: string | null }) {
  if (!value || !href) return null;
  return <a className="inline-flex items-center gap-1 text-primary underline-offset-4 hover:underline" href={href} target="_blank" rel="noreferrer">{label}: {value}<ExternalLink className="h-3 w-3" /></a>;
}

function CoverageMetric({ label, value }: { label: string; value: number }) {
  return <div className="rounded-md border border-border bg-background p-3"><div className="text-xs text-muted-foreground">{label}</div><div className="mt-1 text-lg font-semibold tabular-nums">{value}</div></div>;
}

function ReportSection({ title, value }: { title: string; value: string }) {
  return <section><h2 className="text-sm font-medium">{title}</h2><p className="mt-1 whitespace-pre-wrap leading-6 text-muted-foreground">{value}</p></section>;
}

function formatPublicationDate(citation: Citation) {
  if (citation.publicationYear === null) return null;
  const parts = [citation.publicationYear];
  if (citation.publicationMonth !== null) parts.push(citation.publicationMonth);
  if (citation.publicationDay !== null) parts.push(citation.publicationDay);
  return parts.map((part, index) => index === 0 ? String(part) : String(part).padStart(2, "0")).join("-");
}

function formatInterval(citation: Citation) {
  if (citation.confidenceIntervalLower === null || citation.confidenceIntervalUpper === null) return null;
  return `${citation.confidenceIntervalLower} to ${citation.confidenceIntervalUpper}`;
}

function formatTimestamp(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
