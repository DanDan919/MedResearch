import { problemDetailsSchema } from "./schemas";
import type { ProblemDetails } from "./types";

export type ApiErrorKind = "unauthorized" | "not-found" | "conflict" | "validation" | "server" | "network" | "unexpected";

export class MedResearchApiError extends Error {
  public readonly kind: ApiErrorKind;
  public readonly status?: number;
  public readonly problem?: ProblemDetails;

  public constructor(message: string, kind: ApiErrorKind, status?: number, problem?: ProblemDetails) {
    super(message);
    this.name = "MedResearchApiError";
    this.kind = kind;
    this.status = status;
    this.problem = problem;
  }
}

export function mapStatusToKind(status: number): ApiErrorKind {
  if (status === 401) {
    return "unauthorized";
  }

  if (status === 404) {
    return "not-found";
  }

  if (status === 409) {
    return "conflict";
  }

  if (status === 400) {
    return "validation";
  }

  if (status >= 500) {
    return "server";
  }

  return "unexpected";
}

export async function createApiError(response: Response): Promise<MedResearchApiError> {
  const problem = await readProblemDetails(response);
  const title = (problem?.title ?? response.statusText) || "Request failed";
  return new MedResearchApiError(title, mapStatusToKind(response.status), response.status, problem);
}

async function readProblemDetails(response: Response): Promise<ProblemDetails | undefined> {
  const text = await response.text();
  if (!text) {
    return undefined;
  }

  try {
    return problemDetailsSchema.parse(JSON.parse(text));
  } catch {
    return undefined;
  }
}
