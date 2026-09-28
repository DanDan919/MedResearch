export const queryKeys = {
  health: ["health"] as const,
  research: {
    detail: (researchRunId: string) => ["research", researchRunId] as const,
    report: (researchRunId: string) => ["research", researchRunId, "report"] as const
  }
};
