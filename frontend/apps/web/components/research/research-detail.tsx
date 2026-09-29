"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { Activity, ArrowRight, Database, HeartPulse, TriangleAlert } from "lucide-react";
import { isTerminalResearchStatus } from "@medresearch/api";
import type { ResearchRunProgressResponse } from "@medresearch/api";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@medresearch/ui";
import { useResearchProgress } from "../../lib/api";
import { ErrorPanel, LoadingPanel } from "../state-panel";
import { ProgressPipelineStatus } from "./pipeline-status";
import { ResearchStatusPill } from "./research-status-pill";

export function ResearchDetail({ researchRunId }: { researchRunId: string }) {
  const query = useResearchProgress(researchRunId);

  if (query.isLoading) {
    return <LoadingPanel title="Loading research progress" />;
  }

  if (query.isError || !query.data) {
    return <ErrorPanel title="Research progress unavailable" message="The API could not return this research run progress." />;
  }

  const progress = query.data;
  const terminal = isTerminalResearchStatus(progress.status);

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-normal">Research Run</h1>
          <p className="mt-2 max-w-3xl text-sm text-muted-foreground">{progress.question}</p>
        </div>
        <ResearchStatusPill status={progress.status} />
      </div>

      <ProgressPipelineStatus stages={progress.stages} />

      {progress.failureReason ? (
        <Card className="border-destructive">
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <TriangleAlert className="h-4 w-4 text-destructive" />
              Processing Failed
            </CardTitle>
            <CardDescription>
              The backend stores a safe failure reason, not arbitrary provider payloads or a guessed failed stage.
            </CardDescription>
          </CardHeader>
          <CardContent className="text-sm font-medium">{progress.failureReason}</CardContent>
        </Card>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-3">
        <MetricPanel
          title="Search"
          icon={<Database className="h-4 w-4" />}
          metrics={[
            ["Planned queries", progress.metrics.plannedSearchQueryCount],
            ["Search executions", progress.metrics.literatureSearchCount],
            ["Sources", progress.metrics.literatureSearchSourceCount],
            ["Discovery paths", progress.metrics.discoveryPathCount],
            ["Distinct studies", progress.metrics.distinctDiscoveredStudyCount]
          ]}
        />
        <MetricPanel
          title="Evidence"
          icon={<Activity className="h-4 w-4" />}
          metrics={[
            ["Source materials", progress.metrics.currentSourceMaterialCount],
            ["Structured full text", progress.metrics.structuredFullTextMaterialCount],
            ["Extractions", progress.metrics.evidenceExtractionCount],
            ["Evidence findings", progress.metrics.evidenceFindingCount],
            ["Evaluations", progress.metrics.evidenceEvaluationCount]
          ]}
        />
        <MetricPanel
          title="Synthesis"
          icon={<HeartPulse className="h-4 w-4" />}
          metrics={[
            ["Reports", progress.metrics.researchReportCount],
            ["Claims", progress.metrics.researchReportClaimCount],
            ["Completed extractions", progress.metrics.completedEvidenceExtractionCount],
            ["Skipped extractions", progress.metrics.skippedEvidenceExtractionCount],
            ["Skipped evaluations", progress.metrics.skippedEvidenceEvaluationCount]
          ]}
        />
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Run Metadata</CardTitle>
          <CardDescription>{terminal ? "Processing has stopped." : "This page polls while the run is active."}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 text-sm md:grid-cols-2">
          <Field label="Run ID" value={progress.researchRunId} />
          <Field label="Created" value={progress.createdAt} />
          <Field label="Started" value={progress.startedAt ?? "Not started"} />
          <Field label="Completed" value={progress.completedAt ?? "Not completed"} />
          <Field label="Refreshed" value={progress.refreshedAt} />
          <Field label="Processing lease" value={formatLease(progress)} />
        </CardContent>
      </Card>

      {progress.status === "Completed" ? (
        <Button asChild>
          <Link href={`/research/${progress.researchRunId}/report`}>
            Open Report <ArrowRight className="h-4 w-4" />
          </Link>
        </Button>
      ) : null}
    </div>
  );
}

function MetricPanel({
  title,
  icon,
  metrics
}: {
  title: string;
  icon: ReactNode;
  metrics: ReadonlyArray<readonly [string, number]>;
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          {icon}
          {title}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-2 text-sm">
        {metrics.map(([label, value]) => (
          <div key={label} className="flex items-center justify-between gap-3">
            <span className="text-muted-foreground">{label}</span>
            <span className="font-semibold tabular-nums">{value}</span>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-xs uppercase text-muted-foreground">{label}</div>
      <div className="mt-1 break-words font-medium">{value}</div>
    </div>
  );
}

function formatLease(progress: ResearchRunProgressResponse) {
  const suffix =
    progress.processing.leaseState === "Active" && progress.processing.leaseExpiresAt
      ? ` until ${progress.processing.leaseExpiresAt}`
      : progress.processing.leaseState === "Expired" && progress.processing.lastHeartbeatAt
        ? ` since heartbeat ${progress.processing.lastHeartbeatAt}`
        : "";

  return `${progress.processing.leaseState}${suffix}`;
}
