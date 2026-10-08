import { MedResearchApiClient } from "@medresearch/api";

export function getApiBaseUrl(): string {
  return "/api/backend";
}

export function createApiClient(): MedResearchApiClient {
  return new MedResearchApiClient({ baseUrl: getApiBaseUrl(), fetch: async (input, init) => {
    const response = await fetch(input, { ...init, credentials: "same-origin", cache: "no-store" });
    if (response.status === 401 && typeof window !== "undefined") window.dispatchEvent(new Event("medresearch-session-ended"));
    return response;
  } });
}
