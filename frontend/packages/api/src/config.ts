export const defaultApiBaseUrl = "http://localhost:8080";

export function normalizeApiBaseUrl(value: string | undefined): string {
  const trimmed = value?.trim();

  if (!trimmed) {
    return defaultApiBaseUrl;
  }

  return trimmed.replace(/\/+$/, "");
}
