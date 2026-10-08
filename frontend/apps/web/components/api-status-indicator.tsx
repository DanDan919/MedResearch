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
    return <StatusIndicator compact tone="neutral" label="API Checking" />;
  }

  if (query.data === "connected") {
    return <StatusIndicator compact tone="success" label="API Connected" />;
  }

  return <StatusIndicator compact tone="danger" label="API Unavailable" />;
}
