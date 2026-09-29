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
