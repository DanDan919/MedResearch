"use client";

import { useMemo } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys, shouldPollResearchStatus } from "@medresearch/api";
import type { CreateResearchRequest } from "@medresearch/api";
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
      queryClient.invalidateQueries({ queryKey: queryKeys.research.detail(response.researchRunId) });
    }
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

export function useResearchReport(researchRunId: string, enabled: boolean) {
  const client = useApiClient();

  return useQuery({
    queryKey: queryKeys.research.report(researchRunId),
    queryFn: ({ signal }) => client.getResearchReport(researchRunId, signal),
    enabled
  });
}
