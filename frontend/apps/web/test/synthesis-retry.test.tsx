import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SynthesisRetry } from "../components/research/synthesis-retry";

const id = "11111111-1111-4111-8111-111111111111";
const available = { researchRunId: id, availability: "Available", retryAttemptId: null, attemptStatus: null, generationBudget: 2, generationCallsReserved: 0, failureCategory: null };
function view() {
  return render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><SynthesisRetry researchRunId={id} /></QueryClientProvider>);
}
afterEach(() => vi.unstubAllGlobals());

describe("SynthesisRetry", () => {
  it.each(["Unavailable", "Exhausted"])("does not offer a retry for %s", async availability => {
    const state = availability === "Unavailable" ? { ...available, availability } : { ...available, availability, retryAttemptId: id, attemptStatus: "Failed" };
    vi.stubGlobal("fetch", vi.fn(async () => Response.json(state)));
    view();
    await screen.findByText(availability === "Unavailable" ? "Report retry not available" : "Report retry failed");
    expect(screen.queryByRole("button", { name: "Retry report generation" })).not.toBeInTheDocument();
  });
  it("preserves the same key after an ambiguous HTTP failure and blocks double-clicks", async () => {
    const keys: string[] = [];
    const fetch = vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === "POST") {
        keys.push((init.headers as Record<string, string>)["Idempotency-Key"]);
        return Response.json({ title: "Unavailable" }, { status: 503 });
      }
      return Response.json(available);
    });
    vi.stubGlobal("fetch", fetch);
    view();
    const button = await screen.findByRole("button", { name: "Retry report generation" });
    fireEvent.click(button); fireEvent.click(button);
    await screen.findByRole("alert");
    expect(keys).toHaveLength(1);
    fireEvent.click(button);
    await waitFor(() => expect(keys).toHaveLength(2));
    expect(keys[0]).toBe(keys[1]);
    expect(screen.getByText(/Only report generation is retried/)).toBeInTheDocument();
  });
  it("fails closed on malformed eligibility", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => Response.json({ ...available, generationBudget: 100 })));
    view();
    await screen.findByText("Retry availability could not be verified.");
    expect(screen.queryByRole("button", { name: "Retry report generation" })).not.toBeInTheDocument();
  });
  it.each(["Queued", "Running", "Succeeded", "Failed", "OutcomeUnknown"])("projects persisted %s without another action", async attemptStatus => {
    vi.stubGlobal("fetch", vi.fn(async () => Response.json({ ...available, availability: ["Queued", "Running"].includes(attemptStatus) ? "Active" : "Exhausted", retryAttemptId: id, attemptStatus })));
    view();
    await screen.findByRole("status");
    await waitFor(() => expect(screen.getByRole("status")).not.toHaveTextContent("Checking"));
    expect(screen.queryByRole("button", { name: "Retry report generation" })).not.toBeInTheDocument();
  });
});
