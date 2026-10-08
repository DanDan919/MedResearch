import { describe, expect, it } from "vitest";
import { literatureProviderAttemptSchema } from "../src/schemas";

const base = { attemptId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", researchPlanId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", source: "PubMed", query: "query", status: "Failed", resultCount: null, failureCategory: "NetworkFailure", startedAt: "2026-10-08T00:00:00Z", completedAt: "2026-10-08T00:00:01Z", literatureSearchId: null };

describe("provider attempt runtime contract", () => {
  it.each(["source", "status", "failureCategory"])("rejects an unknown %s", field => {
    expect(literatureProviderAttemptSchema.safeParse({ ...base, [field]: "unknown" }).success).toBe(false);
  });
  it("accepts bounded failure without inventing a result count", () => {
    expect(literatureProviderAttemptSchema.parse(base).resultCount).toBeNull();
  });
  it.each([{ resultCount: 0 }, { completedAt: "invalid" }, { completedAt: null }, { completedAt: "2026-10-07T00:00:00Z" }, { status: "TimedOut" }])("rejects incoherent metadata %j", patch => {
    expect(literatureProviderAttemptSchema.safeParse({ ...base, ...patch }).success).toBe(false);
  });
  it("keeps a zero result distinct from a failure", () => {
    expect(literatureProviderAttemptSchema.parse({ ...base, status: "SucceededZeroResults", resultCount: 0, failureCategory: null, literatureSearchId: base.attemptId }).status).toBe("SucceededZeroResults");
  });
});
