import type { components } from "./generated/medresearch-api";

export type CreateResearchRequest = components["schemas"]["CreateResearchRequest"];
export type CreateResearchResponse = components["schemas"]["CreateResearchResponse"];
export type ResearchRunResponse = components["schemas"]["ResearchRunResponse"];
export type ResearchRunSummaryResponse = components["schemas"]["ResearchRunSummaryResponse"];
export type ResearchRunListResponse = components["schemas"]["ResearchRunListResponse"];
export type ResearchRunStatus = components["schemas"]["ResearchRunStatus"];
export type ResearchReportResponse = components["schemas"]["ResearchReportResponse"];
export type ProblemDetails = components["schemas"]["ProblemDetails"];

export type HealthState = "connected" | "unavailable";

export interface ResearchRunListFilters {
  page?: number;
  pageSize?: number;
  status?: ResearchRunStatus;
}
