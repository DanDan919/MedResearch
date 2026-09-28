"use client";

import { MedResearchApiError } from "@medresearch/api";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@medresearch/ui";
import { useResearchReport } from "../../lib/api";
import { ErrorPanel, LoadingPanel } from "../state-panel";

export function ResearchReport({ researchRunId }: { researchRunId: string }) {
  const query = useResearchReport(researchRunId, true);

  if (query.isLoading) {
    return <LoadingPanel title="Loading report" />;
  }

  if (query.isError || !query.data) {
    const message =
      query.error instanceof MedResearchApiError && query.error.kind === "conflict"
        ? "The backend knows this run, but the report is not ready yet."
        : "The report endpoint did not return a report for this run.";
    return <ErrorPanel title="Report unavailable" message={message} />;
  }

  const report = query.data;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-normal">Research Report</h1>
        <p className="mt-2 max-w-3xl text-sm text-muted-foreground">{report.question}</p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>{report.status}</CardTitle>
          <CardDescription>
            {report.claims.length} claims, {report.coverage.includedStudyCount} included studies
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4 text-sm">
          <Section title="Executive summary" value={report.executiveSummary} />
          <Section title="Evidence summary" value={report.evidenceSummary} />
          <Section title="Conclusion" value={report.conclusion} />
        </CardContent>
      </Card>

      <div className="grid gap-4">
        {report.claims.map((claim) => (
          <Card key={claim.claimId}>
            <CardHeader>
              <CardTitle className="text-base">{claim.claimType}</CardTitle>
              <CardDescription>
                {claim.direction} · {claim.citations.length} citations
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-3 text-sm">
              <p>{claim.text}</p>
              {claim.citations.map((citation) => (
                <div key={`${claim.claimId}-${citation.evidenceId}`} className="rounded-md border border-border p-3">
                  <div className="font-medium">{citation.title}</div>
                  <div className="mt-1 text-xs text-muted-foreground">
                    PMID {citation.pmid ?? "missing"} · DOI {citation.doi ?? "missing"}
                  </div>
                </div>
              ))}
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}

function Section({ title, value }: { title: string; value: string }) {
  return (
    <section>
      <h2 className="text-sm font-medium">{title}</h2>
      <p className="mt-1 text-muted-foreground">{value}</p>
    </section>
  );
}
