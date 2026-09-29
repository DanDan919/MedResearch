"use client";

import { useMemo } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { hasActiveResearchRuns, queryKeys, shouldPollResearchStatus } from "@medresearch/api";
import type { CreateResearchRequest, ResearchRunListFilters } from "@medresearch/api";
import { createApiClient } from "../components/api-client-provider";

export function useApiClient() {
  return useMemo(() => createApiClient(), []);
}

export function useCreateResearch() {
  const client = useApiClient();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateResearchRequest) => client.createResearch(request),
    onSuccess: (response) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.research.list() });
      queryClient.invalidateQueries({ queryKey: queryKeys.research.detail(response.researchRunId) });
    }
  });
}

export function useResearchRuns(filters: ResearchRunListFilters) {
  const client = useApiClient();

  return useQuery({
    queryKey: queryKeys.research.list(filters),
    queryFn: ({ signal }) => client.listResearchRuns(filters, signal),
    refetchInterval: (query) =>
      query.state.data && hasActiveResearchRuns(query.state.data.items.map((item) => item.status)) ? 10_000 : false
  });
}

export function useResearchRun(researchRunId: string) {
  const client = useApiClient();

  return useQuery({
    queryKey: queryKeys.research.detail(researchRunId),
    queryFn: ({ signal }) => client.getResearchRun(researchRunId, signal),
    enabled: researchRunId.length > 0,
    refetchInterval: (query) => (shouldPollResearchStatus(query.state.data?.status) ? 5_000 : false)
  });
}

export function useResearchProgress(researchRunId: string) {
  const client = useApiClient();

  return useQuery({
    queryKey: queryKeys.research.progress(researchRunId),
    queryFn: ({ signal }) => client.getResearchProgress(researchRunId, signal),
    enabled: researchRunId.length > 0,
    refetchInterval: (query) => (shouldPollResearchStatus(query.state.data?.status) ? 5_000 : false)
  });
}

export function useResearchReport(researchRunId: string, enabled: boolean) {
  const client = useApiClient();

  return useQuery({
    queryKey: queryKeys.research.report(researchRunId),
    queryFn: ({ signal }) => client.getResearchReport(researchRunId, signal),
    enabled
  });
}
