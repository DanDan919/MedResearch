import { normalizeApiBaseUrl } from "./config";
import { createApiError, MedResearchApiError } from "./errors";
import {
  createResearchResponseSchema,
  researchRunListResponseSchema,
  researchReportResponseSchema,
  researchRunResponseSchema
} from "./schemas";
import type {
  CreateResearchRequest,
  CreateResearchResponse,
  HealthState,
  ResearchReportResponse,
  ResearchRunListFilters,
  ResearchRunListResponse,
  ResearchRunResponse
} from "./types";

export interface MedResearchApiClientOptions {
  baseUrl?: string;
  fetch?: typeof fetch;
}

export class MedResearchApiClient {
  private readonly baseUrl: string;
  private readonly fetchImpl: typeof fetch;

  public constructor(options: MedResearchApiClientOptions = {}) {
    this.baseUrl = normalizeApiBaseUrl(options.baseUrl);
    this.fetchImpl = options.fetch ?? fetch;
  }

  public async getReadyHealth(signal?: AbortSignal): Promise<HealthState> {
    try {
      const response = await this.fetchImpl(this.url("/health/ready"), {
        method: "GET",
        signal,
        headers: { Accept: "text/plain, application/json" }
      });

      return response.ok ? "connected" : "unavailable";
    } catch {
      return "unavailable";
    }
  }

  public async createResearch(
    request: CreateResearchRequest,
    signal?: AbortSignal
  ): Promise<CreateResearchResponse> {
    return this.requestJson("/api/research", createResearchResponseSchema.parse, {
      method: "POST",
      signal,
      headers: {
        "Content-Type": "application/json",
        Accept: "application/json"
      },
      body: JSON.stringify(request)
    });
  }

  public async listResearchRuns(
    filters: ResearchRunListFilters = {},
    signal?: AbortSignal
  ): Promise<ResearchRunListResponse> {
    const searchParams = new URLSearchParams();
    searchParams.set("page", String(filters.page ?? 1));
    searchParams.set("pageSize", String(filters.pageSize ?? 20));
    if (filters.status) {
      searchParams.set("status", filters.status);
    }

    return this.requestJson(
      `/api/research?${searchParams.toString()}`,
      researchRunListResponseSchema.parse,
      {
        method: "GET",
        signal,
        headers: { Accept: "application/json" }
      }
    );
  }

  public async getResearchRun(
    researchRunId: string,
    signal?: AbortSignal
  ): Promise<ResearchRunResponse> {
    return this.requestJson(
      `/api/research/${encodeURIComponent(researchRunId)}`,
      researchRunResponseSchema.parse,
      {
        method: "GET",
        signal,
        headers: { Accept: "application/json" }
      }
    );
  }

  public async getResearchReport(
    researchRunId: string,
    signal?: AbortSignal
  ): Promise<ResearchReportResponse> {
    return this.requestJson(
      `/api/research/${encodeURIComponent(researchRunId)}/report`,
      researchReportResponseSchema.parse,
      {
        method: "GET",
        signal,
        headers: { Accept: "application/json" }
      }
    );
  }

  private async requestJson<T>(
    path: string,
    parse: (value: unknown) => T,
    init: RequestInit
  ): Promise<T> {
    let response: Response;
    try {
      response = await this.fetchImpl(this.url(path), init);
    } catch (error) {
      throw new MedResearchApiError(
        error instanceof Error ? error.message : "Network request failed",
        "network"
      );
    }

    if (!response.ok) {
      throw await createApiError(response);
    }

    return parse(await response.json());
  }

  private url(path: string): string {
    return `${this.baseUrl}${path}`;
  }
}
