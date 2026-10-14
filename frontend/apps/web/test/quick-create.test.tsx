import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { webcrypto } from "node:crypto";
import { QuickCreate } from "../components/quick/quick-create";
import { WebSessionContext } from "../components/web-session-provider";
import { quickProcessing, quickRunId } from "../../../packages/api/test/fixtures/quick";

const push = vi.hoisted(() => vi.fn()); vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
function view(authenticated = true) {
  return render(<QueryClientProvider client={new QueryClient()}><WebSessionContext value={{ session: { authenticated, mode: "oidc", sessionId: authenticated ? "A" : null,
    displayName: "User A", expiresAt: null }, endSession: () => undefined }}><QuickCreate /></WebSessionContext></QueryClientProvider>);
}
beforeEach(() => { sessionStorage.clear(); vi.stubGlobal("crypto", webcrypto); });
afterEach(() => { cleanup(); sessionStorage.clear(); vi.unstubAllGlobals(); push.mockClear(); });
describe("Quick create", () => {
  it("accepts RU question through one POST and navigates to its actual run, blocking double submit", async () => {
    let finish!: (value: Response) => void;
    const mock = vi.fn(() => new Promise<Response>(resolve => { finish = resolve; })); vi.stubGlobal("fetch", mock);
    view(); fireEvent.change(screen.getByLabelText("Your question"), { target: { value: "Влияет ли сон на память?" } });
    fireEvent.click(screen.getByRole("button", { name: "Ask question" })); fireEvent.click(screen.getByRole("button", { name: "Ask question" }));
    await waitFor(() => expect(mock).toHaveBeenCalledTimes(1)); expect(screen.getByRole("button", { name: "Ask question" })).toBeDisabled();
    finish(Response.json({ ...quickProcessing(), status: "Queued", startedAt: null }, { status: 202 }));
    await waitFor(() => expect(push).toHaveBeenCalledWith(`/quick-research/${quickRunId}`));
  });
  it("replays an ambiguous POST with the same key and does not automatically retry", async () => {
    const mock = vi.fn<typeof fetch>(async () => Response.json({ title: "Private backend error" }, { status: 503 })); vi.stubGlobal("fetch", mock);
    view(); fireEvent.change(screen.getByLabelText("Your question"), { target: { value: "Does sleep affect memory?" } });
    fireEvent.click(screen.getByRole("button", { name: "Ask question" }));
    await screen.findByText(/Could not confirm whether your question was saved/);
    expect(screen.getByRole("button", { name: "Retry submission" })).toBeEnabled();
    expect(screen.getByRole("link", { name: "Check history" })).toHaveAttribute("href", "/quick-research");
    expect(screen.queryByText("Private backend error")).not.toBeInTheDocument(); expect(mock).toHaveBeenCalledTimes(1);
    const key = new Headers(mock.mock.calls[0][1]?.headers).get("idempotency-key");
    fireEvent.click(screen.getByRole("button", { name: "Retry submission" }));
    await waitFor(() => expect(mock).toHaveBeenCalledTimes(2));
    expect(new Headers(mock.mock.calls[1][1]?.headers).get("idempotency-key")).toBe(key);
  });
  it("allows a corrected question after an explicit validation rejection", async () => {
    const mock = vi.fn(async () => Response.json({ title: "Validation" }, { status: 400 })); vi.stubGlobal("fetch", mock);
    view(); fireEvent.change(screen.getByLabelText("Your question"), { target: { value: "Question" } }); fireEvent.click(screen.getByRole("button", { name: "Ask question" }));
    await screen.findByText(/question was not accepted/); expect(screen.getByRole("button", { name: "Ask question" })).toBeEnabled();
  });
  it("aborts on leaving the account and does not navigate from the stale response", async () => {
    let finish!: (value: Response) => void;
    const mock = vi.fn<typeof fetch>(() => new Promise<Response>(resolve => { finish = resolve; })); vi.stubGlobal("fetch", mock);
    const v = view(); fireEvent.change(screen.getByLabelText("Your question"), { target: { value: "Question" } }); fireEvent.click(screen.getByRole("button", { name: "Ask question" }));
    await waitFor(() => expect(mock).toHaveBeenCalledTimes(1));
    const signal = mock.mock.calls[0][1]!.signal!; v.unmount(); expect(signal.aborted).toBe(true);
    finish(Response.json({ ...quickProcessing(), status: "Queued", startedAt: null }, { status: 202 }));
    await waitFor(() => expect(push).not.toHaveBeenCalled());
  });
  it("anonymous users cannot submit", () => {
    const mock = vi.fn(); vi.stubGlobal("fetch", mock); view(false);
    expect(screen.getByText("Sign in required")).toBeInTheDocument(); expect(screen.queryByRole("button", { name: "Ask question" })).not.toBeInTheDocument(); expect(mock).not.toHaveBeenCalled();
  });
});
