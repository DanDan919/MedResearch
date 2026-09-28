import type { ResearchRunStatus } from "./types";

const terminalStatuses: ReadonlySet<ResearchRunStatus> = new Set(["Completed", "Failed", "Cancelled"]);

export const researchPipelineStages: readonly ResearchRunStatus[] = [
  "Queued",
  "Planning",
  "Searching",
  "Extracting",
  "Evaluating",
  "Synthesizing",
  "Completed"
];

export function isTerminalResearchStatus(status: ResearchRunStatus): boolean {
  return terminalStatuses.has(status);
}

export const researchRunStatusPresentation: Record<
  ResearchRunStatus,
  {
    label: string;
    tone: "neutral" | "success" | "warning" | "danger" | "info";
    terminal: boolean;
  }
> = {
  Queued: { label: "Queued", tone: "neutral", terminal: false },
  Planning: { label: "Planning", tone: "info", terminal: false },
  Searching: { label: "Searching", tone: "info", terminal: false },
  Extracting: { label: "Extracting", tone: "info", terminal: false },
  Evaluating: { label: "Evaluating", tone: "info", terminal: false },
  Synthesizing: { label: "Synthesizing", tone: "info", terminal: false },
  Completed: { label: "Completed", tone: "success", terminal: true },
  Failed: { label: "Failed", tone: "danger", terminal: true },
  Cancelled: { label: "Cancelled", tone: "warning", terminal: true }
};

export function hasActiveResearchRuns(statuses: readonly ResearchRunStatus[]): boolean {
  return statuses.some((status) => !isTerminalResearchStatus(status));
}

export function shouldPollResearchStatus(status: ResearchRunStatus | undefined): boolean {
  return status === undefined ? false : !isTerminalResearchStatus(status);
}

export function stageState(
  stage: ResearchRunStatus,
  currentStatus: ResearchRunStatus
): "complete" | "current" | "pending" | "failed" | "cancelled" {
  if (currentStatus === "Failed") {
    return stage === "Completed" ? "pending" : "failed";
  }

  if (currentStatus === "Cancelled") {
    return stage === "Completed" ? "pending" : "cancelled";
  }

  if (stage === currentStatus) {
    return "current";
  }

  const stageIndex = researchPipelineStages.indexOf(stage);
  const currentIndex = researchPipelineStages.indexOf(currentStatus);
  return stageIndex >= 0 && currentIndex >= 0 && stageIndex < currentIndex ? "complete" : "pending";
}
