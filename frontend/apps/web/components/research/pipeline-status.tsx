import { CheckCircle2, Circle, CircleAlert, Loader2, XCircle } from "lucide-react";
import { cn } from "@medresearch/ui";
import { researchPipelineStages, stageState } from "@medresearch/api";
import type { ResearchRunStatus } from "@medresearch/api";

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
