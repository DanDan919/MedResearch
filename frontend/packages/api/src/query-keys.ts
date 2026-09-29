import type { ResearchRunListFilters } from "./types";

export function normalizeResearchRunListFilters(filters: ResearchRunListFilters = {}) {
  return {
    page: filters.page ?? 1,
    pageSize: filters.pageSize ?? 20,
    status: filters.status ?? "All"
  } as const;
}

export const queryKeys = {
  health: ["health"] as const,
  research: {
    list: (filters: ResearchRunListFilters = {}) => {
      const normalized = normalizeResearchRunListFilters(filters);
      return ["research", "list", normalized.page, normalized.pageSize, normalized.status] as const;
    },
    detail: (researchRunId: string) => ["research", researchRunId] as const,
    progress: (researchRunId: string) => ["research", researchRunId, "progress"] as const,
    report: (researchRunId: string) => ["research", researchRunId, "report"] as const
  }
};
