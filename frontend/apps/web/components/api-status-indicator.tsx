"use client";

import { useQuery } from "@tanstack/react-query";
import { queryKeys } from "@medresearch/api";
import { StatusIndicator } from "@medresearch/ui";
import { createApiClient } from "./api-client-provider";

export function ApiStatusIndicator() {
  const query = useQuery({
    queryKey: queryKeys.health,
    queryFn: ({ signal }) => createApiClient().getReadyHealth(signal),
    refetchInterval: 30_000,
    retry: 1
  });

  if (query.isLoading) {
    return <StatusIndicator tone="neutral" label="API Checking" aria-live="polite" />;
  }

  if (query.data === "connected") {
    return <StatusIndicator tone="success" label="API Connected" aria-live="polite" />;
  }

  return <StatusIndicator tone="danger" label="API Unavailable" aria-live="polite" />;
}
