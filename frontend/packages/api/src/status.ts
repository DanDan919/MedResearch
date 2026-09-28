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
