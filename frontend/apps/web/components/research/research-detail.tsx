"use client";

import Link from "next/link";
import { ArrowRight } from "lucide-react";
import { isTerminalResearchStatus } from "@medresearch/api";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@medresearch/ui";
import { useResearchRun } from "../../lib/api";
import { ErrorPanel, LoadingPanel } from "../state-panel";
import { PipelineStatus } from "./pipeline-status";
import { ResearchStatusPill } from "./research-status-pill";

export function ResearchDetail({ researchRunId }: { researchRunId: string }) {
  const query = useResearchRun(researchRunId);

  if (query.isLoading) {
    return <LoadingPanel title="Loading research run" />;
  }

  if (query.isError || !query.data) {
    return <ErrorPanel title="Research run unavailable" message="The API could not return this research run." />;
  }

  const run = query.data;
  const terminal = isTerminalResearchStatus(run.status);

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-normal">Research Run</h1>
          <p className="mt-2 max-w-3xl text-sm text-muted-foreground">{run.question}</p>
        </div>
        <ResearchStatusPill status={run.status} />
      </div>

      <PipelineStatus status={run.status} />

      <Card>
        <CardHeader>
          <CardTitle>Run Metadata</CardTitle>
          <CardDescription>{terminal ? "Processing has stopped." : "This page polls while the run is active."}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 text-sm md:grid-cols-2">
          <Field label="Run ID" value={run.researchRunId} />
          <Field label="Created" value={run.createdAt} />
          <Field label="Started" value={run.startedAt ?? "Not started"} />
          <Field label="Completed" value={run.completedAt ?? "Not completed"} />
          {run.failureReason ? <Field label="Failure" value={run.failureReason} /> : null}
        </CardContent>
      </Card>

      {run.status === "Completed" ? (
        <Button asChild>
          <Link href={`/research/${run.researchRunId}/report`}>
            Open Report <ArrowRight className="h-4 w-4" />
          </Link>
        </Button>
      ) : null}
    </div>
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
