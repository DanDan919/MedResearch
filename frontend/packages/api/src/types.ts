import type { components } from "./generated/medresearch-api";
import { researchProvenanceResponseSchema, researchReportResponseSchema, researchRunProgressResponseSchema } from "./schemas";
import type { z } from "zod";

export type CreateResearchRequest = components["schemas"]["CreateResearchRequest"];
export type CreateResearchResponse = components["schemas"]["CreateResearchResponse"];
export type ResearchRunResponse = components["schemas"]["ResearchRunResponse"];
export type ResearchRunSummaryResponse = components["schemas"]["ResearchRunSummaryResponse"];
export type ResearchRunListResponse = components["schemas"]["ResearchRunListResponse"];
export type ResearchRunProgressResponse = z.infer<typeof researchRunProgressResponseSchema>;
export type ResearchRunStatus = ResearchRunResponse["status"];
export type ResearchReportResponse = z.infer<typeof researchReportResponseSchema>;
export type QuantitativeSynthesisArtifactResponse = components["schemas"]["QuantitativeSynthesisArtifactResponse"];
export type ResearchProvenanceResponse = z.infer<typeof researchProvenanceResponseSchema>;
export type ProblemDetails = components["schemas"]["ProblemDetails"];

export type HealthState = "connected" | "unavailable";

export interface ResearchRunListFilters {
  page?: number;
  pageSize?: number;
  status?: ResearchRunStatus;
}
