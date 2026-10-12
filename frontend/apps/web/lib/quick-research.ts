"use client";

import { useQuery } from "@tanstack/react-query";
import { quickQueryKeys } from "@medresearch/api";
import { useApiClient } from "./api";
import { useWebSession } from "../components/web-session-provider";

export function useQuickResearch(runId: string) {
  const client = useApiClient();
  const { session } = useWebSession();
  return useQuery({ queryKey: quickQueryKeys.detail(session.sessionId, runId),
    queryFn: ({ signal }) => client.getQuickResearch(runId, signal), enabled: session.authenticated,
    retry: false, refetchInterval: query => ["Queued", "Processing"].includes(query.state.data?.status ?? "") ? 5000 : false });
}

export function useQuickHistory(page: number) {
  const client = useApiClient();
  const { session } = useWebSession();
  return useQuery({ queryKey: quickQueryKeys.history(session.sessionId, page),
    queryFn: ({ signal }) => client.listQuickResearch(page, signal), enabled: session.authenticated, retry: false,
    refetchInterval: query => query.state.data?.items.some(item => ["Queued", "Processing"].includes(item.status)) ? 10000 : false });
}
