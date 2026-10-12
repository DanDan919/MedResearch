import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { QuickResult, QuickResultWorkspace } from "../components/quick/quick-result-workspace";
import { QuickHistory } from "../components/quick/quick-history";
import { WebSessionContext } from "../components/web-session-provider";
import { quickDetails, quickHistory, quickInsufficient, quickProcessing, quickRunId, otherQuickRunId, shortAnswer, excerpt,
  quickWithContradiction } from "../../../packages/api/test/fixtures/quick";
import type { PublicSession } from "../lib/auth/types";

const session: PublicSession = { authenticated: true, mode: "oidc", sessionId: "A", displayName: "User A", expiresAt: null };
function wrapper(children: React.ReactNode, client = new QueryClient({ defaultOptions: { queries: { retry: false } } }), current = session) {
  return <QueryClientProvider client={client}><WebSessionContext value={{ session: current, endSession: () => undefined }}>{children}</WebSessionContext></QueryClientProvider>;
}
const view = (details = quickDetails()) => render(<QuickResult details={details} refresh={() => undefined} />);
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

describe("Quick result presentation", () => {
  it("shows the saved answer, exact citations and source metadata, with details closed", () => {
    view();
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Synthetic question");
    expect(screen.getAllByText(shortAnswer)).toHaveLength(2);
    expect(screen.queryByRole("heading", { name: "Where findings differ" })).not.toBeInTheDocument();
    expect(screen.getByText("Technical details").closest("details")).not.toHaveAttribute("open");
    fireEvent.click(screen.getByRole("button", { name: "View source 1 and supporting excerpt" }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText(excerpt)).toBeInTheDocument();
    expect(within(dialog).getByText("Synthetic Author")).toBeInTheDocument();
    expect(within(dialog).getByText("PMC1234567")).toBeInTheDocument();
    expect(within(dialog).getByRole("link", { name: "Open publication" })).toHaveAttribute("href", "https://pubmed.ncbi.nlm.nih.gov/12345678/");
    expect(within(dialog).getByRole("link")).toHaveAttribute("rel", "noopener noreferrer");
  });
  it("renders only saved contradictions and their two distinct source references", () => {
    view(quickWithContradiction());
    expect(screen.getByRole("heading", { name: "Where findings differ" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "View source 2 and supporting excerpt" }));
    expect(within(screen.getByRole("dialog")).getByText("The synthetic second source reports different findings in adults.")).toBeInTheDocument();
  });
  it("leaves missing metadata absent and preserves year-only date precision", () => {
    const d = quickDetails(); const s = d.retrieval!.sources[0];
    s.journal = null; s.publicationYear = null; s.authors = []; s.doi = null; s.pmcid = null; s.abstract = null; s.url = null;
    view(d);
    fireEvent.click(screen.getByRole("button", { name: s.title }));
    const dialog = within(screen.getByRole("dialog"));
    expect(dialog.getByText("Publication details not reported.")).toBeInTheDocument();
    expect(dialog.getByText("Publication link unavailable.")).toBeInTheDocument();
    expect(dialog.queryByRole("link")).not.toBeInTheDocument();
    expect(dialog.queryByText("DOI:")).not.toBeInTheDocument();
    expect(screen.queryByText(/Jan/)).not.toBeInTheDocument();
  });
  it("shows insufficient evidence without inventing findings", () => {
    view(quickInsufficient());
    expect(screen.getByRole("heading", { name: "Insufficient evidence" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Findings" })).not.toBeInTheDocument();
    expect(screen.queryByText(shortAnswer)).not.toBeInTheDocument();
  });
  it.each(["Queued", "Processing"] as const)("shows three unquantified phases for %s", status => {
    const d = quickProcessing(); d.status = status;
    view(d);
    const progress = screen.getByRole("list", { name: "Quick research progress" });
    expect(within(progress).getAllByRole("listitem")).toHaveLength(3);
    expect(progress).not.toHaveTextContent("complete");
    expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();
  });
  it("does not pretend comparison has already completed during the single Q2 answer call", () => {
    view(quickProcessing(true));
    const phases = within(screen.getByRole("list", { name: "Quick research progress" })).getAllByRole("listitem");
    expect(phases[0]).toHaveTextContent("complete");
    expect(phases[1]).toHaveAttribute("aria-current", "step"); expect(phases[2]).toHaveAttribute("aria-current", "step");
  });
  it("retains sources on failure and does not expose raw diagnostics", () => {
    const d = quickDetails(); d.status = "Failed"; d.answer = null; d.answerStatus = "Failed"; d.safeError = "private-stack-secret";
    view(d);
    expect(screen.getByRole("alert")).toHaveTextContent("Answer unavailable");
    expect(screen.getByText(d.retrieval!.sources[0].title)).toBeInTheDocument();
    expect(screen.queryByText("private-stack-secret")).not.toBeInTheDocument();
  });
  it("does not render Q0 synthetic result as medical facts", () => {
    const d = quickDetails(); d.answer = null; d.answerStatus = null; d.retrieval = null;
    d.result = { quickRunId, isSynthetic: true, summary: "Synthetic fake treatment", facts: [], disagreements: [], limitations: [], sources: [] };
    view(d);
    expect(screen.getByText("No answer available")).toBeInTheDocument();
    expect(screen.queryByText("Synthetic fake treatment")).not.toBeInTheDocument();
  });
  it.each(["ZeroResults", "NeedsClarification"] as const)("honestly explains %s", outcome => {
    const d = quickDetails(); d.answer = null; d.answerStatus = null; d.retrieval!.sources = []; d.retrieval!.outcome = outcome;
    view(d);
    expect(screen.getByText("No answer available")).toBeInTheDocument();
    expect(screen.getByText(outcome === "ZeroResults" ? /does not establish that no research exists/ : /without guessing its meaning/)).toBeInTheDocument();
  });
  it("shows incomplete coverage without exposing private provider errors", () => {
    const d = quickDetails(); d.retrieval!.outcome = "Partial"; d.retrieval!.attempts[1].outcome = "TimedOut";
    d.retrieval!.attempts[1].returnedCount = null; d.retrieval!.attempts[1].failureCategory = "private-provider-error";
    view(d);
    expect(screen.getByText(/Search coverage is incomplete/)).toBeInTheDocument();
    fireEvent.click(screen.getByText("Technical details"));
    expect(screen.getByText("EuropePmc: Timed out")).toBeInTheDocument();
    expect(screen.queryByText("private-provider-error")).not.toBeInTheDocument();
  });
  it("renders LLM text as text, not HTML, and refuses unsafe source links", () => {
    const d = quickDetails(); const payload = '<img src=x onerror="alert(1)">';
    d.answer!.shortAnswer = payload; d.answer!.findings[0].text = payload;
    d.retrieval!.sources[0].url = "javascript:alert(1)";
    const { container } = view(d);
    expect(container.querySelector("img")).toBeNull();
    expect(screen.getAllByText(payload)).toHaveLength(2);
    fireEvent.click(screen.getByRole("button", { name: "View source 1 and supporting excerpt" }));
    expect(within(screen.getByRole("dialog")).queryByRole("link")).not.toBeInTheDocument();
  });
});

describe("Quick private data queries", () => {
  it.each([404, 503])( "sanitizes HTTP %i", async status => {
    vi.stubGlobal("fetch", vi.fn(async () => Response.json({ title: "private diagnostics" }, { status })));
    render(wrapper(<QuickResultWorkspace runId={quickRunId} />));
    await screen.findByRole("alert");
    expect(screen.queryByText("private diagnostics")).not.toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent(status === 404 ? "Result not available" : "Could not load");
  });
  it("removes old answer and open excerpt on account or run change", async () => {
    const mock = vi.fn(async () => Response.json(quickDetails())); vi.stubGlobal("fetch", mock);
    const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
    const result = render(wrapper(<QuickResultWorkspace runId={quickRunId} />, client));
    fireEvent.click(await screen.findByRole("button", { name: "View source 1 and supporting excerpt" }));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    mock.mockImplementation(async () => Response.json({ title: "not found" }, { status: 404 }));
    result.rerender(wrapper(<QuickResultWorkspace runId={quickRunId} />, client, { ...session, sessionId: "B", displayName: "User B" }));
    expect(screen.queryByText(shortAnswer)).not.toBeInTheDocument(); expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    await screen.findByText("Result not available");
    result.rerender(wrapper(<QuickResultWorkspace runId={otherQuickRunId} />, client));
    expect(screen.queryByText(shortAnswer)).not.toBeInTheDocument();
    await screen.findByText("Result not available");
    expect(mock).toHaveBeenCalledTimes(3);
  });
  it("does not fetch when signed out", () => {
    const mock = vi.fn(); vi.stubGlobal("fetch", mock);
    render(wrapper(<QuickResultWorkspace runId={quickRunId} />, undefined, { ...session, authenticated: false, sessionId: null }));
    expect(screen.getByText("Sign in required")).toBeInTheDocument(); expect(mock).not.toHaveBeenCalled();
  });
  it("history is owner-scoped GET-only and opens the scoped result", async () => {
    const mock = vi.fn(async () => Response.json(quickHistory())); vi.stubGlobal("fetch", mock);
    render(wrapper(<QuickHistory />));
    const link = await screen.findByRole("link", { name: /Synthetic question/ });
    expect(link).toHaveAttribute("href", `/quick-research/${quickRunId}`);
    await waitFor(() => expect(mock).toHaveBeenCalledTimes(1));
    expect(mock.mock.calls[0]).toEqual(["/api/backend/api/quick-research?page=1&pageSize=20", expect.objectContaining({ method: "GET", cache: "no-store", credentials: "same-origin" })]);
    expect(screen.queryByRole("button", { name: /start|create/i })).not.toBeInTheDocument();
  });
});
