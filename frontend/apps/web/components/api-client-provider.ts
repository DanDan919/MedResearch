import { MedResearchApiClient, normalizeApiBaseUrl } from "@medresearch/api";

export function getApiBaseUrl(): string {
  return normalizeApiBaseUrl(process.env.NEXT_PUBLIC_MEDRESEARCH_API_URL);
}

export function createApiClient(): MedResearchApiClient {
  return new MedResearchApiClient({ baseUrl: getApiBaseUrl() });
}
