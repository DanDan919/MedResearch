import { CheckCircle2, Circle, CircleAlert, Loader2, XCircle } from "lucide-react";
import { cn } from "@medresearch/ui";
import { researchPipelineStages, stageState } from "@medresearch/api";
import type { ResearchRunProgressResponse, ResearchRunStatus } from "@medresearch/api";

const iconByState = {
  complete: CheckCircle2,
  current: Loader2,
  pending: Circle,
  failed: XCircle,
  cancelled: CircleAlert
} as const;

export function PipelineStatus({ status }: { status: ResearchRunStatus }) {
  return (
    <ol className="grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
      {researchPipelineStages.map((stage) => {
        const state = stageState(stage, status);
        const Icon = iconByState[state];
        return (
          <li
            key={stage}
            className={cn(
              "flex min-h-12 items-center gap-2 rounded-md border border-border bg-surface px-3 text-sm",
              state === "current" && "border-primary",
              state === "failed" && "border-destructive",
              state === "cancelled" && "border-warning"
            )}
          >
            <Icon className={cn("h-4 w-4", state === "current" && "animate-spin text-primary")} />
            <span>{stage}</span>
          </li>
        );
      })}
    </ol>
  );
}

const progressIconByState = {
  Completed: CheckCircle2,
  Current: Loader2,
  Pending: Circle
} as const;

const phases = [
  { label: "Finding sources", stages: ["Queued", "Planning", "Searching"] },
  { label: "Analyzing evidence", stages: ["Extracting", "Evaluating"] },
  { label: "Preparing report", stages: ["Synthesizing", "Completed"] }
] as const;

export function ResearchPhases({ progress }: { progress: ResearchRunProgressResponse }) {
  const terminalFailure = progress.status === "Failed" || progress.status === "Cancelled";
  return <div className="space-y-3">
    <ol aria-label="Research phases" className="grid gap-3 sm:grid-cols-3">
      {phases.map(phase => {
        const stages = progress.stages.filter(stage => (phase.stages as readonly string[]).includes(stage.stage));
        const hasSavedOutput = stages.some(stage => stage.stage !== "Queued" && stage.metrics.some(metric => metric.value > 0));
        const state = terminalFailure ? hasSavedOutput ? "saved" : "unavailable"
          : stages.some(stage => stage.state === "Current") ? "current"
          : stages.length === phase.stages.length && stages.every(stage => stage.state === "Completed") ? "complete" : "pending";
        const Icon = state === "current" ? Loader2 : state === "complete" ? CheckCircle2 : state === "unavailable" ? CircleAlert : Circle;
        return <li key={phase.label} data-state={state} className={cn("min-h-16 border-b border-border py-3 text-sm", state === "current" && "border-primary")}>
          <div className="flex items-center gap-2 font-medium"><Icon className={cn("h-4 w-4 shrink-0", state === "current" && "animate-spin text-primary")} />{phase.label}</div>
          <p className="mt-1 text-xs text-muted-foreground">{state === "saved" ? "Saved results" : state === "unavailable" ? "Unavailable" : state === "current" ? "In progress" : state === "complete" ? "Completed" : "Pending"}</p>
        </li>;
      })}
    </ol>
    <details className="border-b border-border pb-3">
      <summary className="cursor-pointer text-sm text-muted-foreground">Technical stages</summary>
      <div className="mt-3"><ProgressPipelineStatus stages={progress.stages} /></div>
    </details>
  </div>;
}

export function ProgressPipelineStatus({ stages }: { stages: ResearchRunProgressResponse["stages"] }) {
  return (
    <ol className="grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
      {stages.map((stage) => {
        const Icon = progressIconByState[stage.state];
        return (
          <li
            key={stage.stage}
            className={cn(
              "min-h-24 rounded-md border border-border bg-surface px-3 py-3 text-sm",
              stage.state === "Current" && "border-primary"
            )}
          >
            <div className="flex items-center gap-2 font-medium">
              <Icon className={cn("h-4 w-4", stage.state === "Current" && "animate-spin text-primary")} />
              <span>{stage.stage}</span>
            </div>
            <dl className="mt-3 grid gap-1 text-xs">
              {stage.metrics.map((metric) => (
                <div key={metric.label} className="flex items-center justify-between gap-3">
                  <dt className="text-muted-foreground">{metric.label}</dt>
                  <dd className="font-semibold tabular-nums">{metric.value}</dd>
                </div>
              ))}
            </dl>
          </li>
        );
      })}
    </ol>
  );
}
