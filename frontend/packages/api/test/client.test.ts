import { describe, expect, it, vi } from "vitest";
import { MedResearchApiClient } from "../src/client";

describe("MedResearchApiClient research history", () => {
  it("requests paged research runs with default filters", async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      jsonResponse({
        items: [],
        page: 1,
        pageSize: 20,
        totalCount: 0,
        totalPages: 0
      })
    );
    const client = new MedResearchApiClient({ baseUrl: "https://api.example.test/", fetch: fetchMock });

    const result = await client.listResearchRuns();

    expect(result.totalCount).toBe(0);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.example.test/api/research?page=1&pageSize=20");
    expect(init?.method).toBe("GET");
  });

  it("encodes explicit pagination and status filters", async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      jsonResponse({
        items: [
          {
            researchRunId: "11111111-1111-4111-8111-111111111111",
            researchQuestionId: "22222222-2222-4222-8222-222222222222",
            question: "Does sleep deprivation impair memory?",
            status: "Completed",
            createdAt: "2026-09-28T12:00:00Z",
            startedAt: "2026-09-28T12:01:00Z",
            completedAt: "2026-09-28T12:05:00Z",
            failureReason: null
          }
        ],
        page: 2,
        pageSize: 10,
        totalCount: 11,
        totalPages: 2
      })
    );
    const client = new MedResearchApiClient({ baseUrl: "https://api.example.test", fetch: fetchMock });

    const result = await client.listResearchRuns({ page: 2, pageSize: 10, status: "Completed" });

    expect(result.items[0].status).toBe("Completed");
    const [url] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.example.test/api/research?page=2&pageSize=10&status=Completed");
  });
});

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
}
