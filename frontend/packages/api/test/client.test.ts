import { describe, expect, it, vi } from "vitest";
import { MedResearchApiClient, MedResearchApiError } from "../src";

describe("MedResearchApiClient", () => {
  it("creates a research run through the real backend route shape", async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(JSON.stringify({ researchRunId: "11111111-1111-4111-8111-111111111111", status: "Queued" }), {
        status: 201,
        headers: { "Content-Type": "application/json" }
      })
    );
    const client = new MedResearchApiClient({ baseUrl: "http://api.test", fetch: fetchMock });

    const result = await client.createResearch({ question: "Does sleep affect memory?" });

    expect(result.status).toBe("Queued");
    expect(fetchMock).toHaveBeenCalledWith(
      "http://api.test/api/research",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ question: "Does sleep affect memory?" })
      })
    );
  });

  it("maps backend failures to typed errors", async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(JSON.stringify({ title: "Research report is not ready", status: 409 }), {
        status: 409,
        headers: { "Content-Type": "application/problem+json" }
      })
    );
    const client = new MedResearchApiClient({ baseUrl: "http://api.test", fetch: fetchMock });

    await expect(client.getResearchReport("11111111-1111-4111-8111-111111111111")).rejects.toMatchObject({
      kind: "conflict",
      status: 409
    } satisfies Partial<MedResearchApiError>);
  });
});
