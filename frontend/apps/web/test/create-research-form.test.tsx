import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CreateResearchForm } from "../components/research/create-research-form";

const push = vi.fn();
const fetchMock = vi.fn<typeof fetch>();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push })
}));

describe("CreateResearchForm", () => {
  beforeEach(() => {
    push.mockReset();
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
  });

  it("validates short questions without calling the backend", async () => {
    renderWithClient(<CreateResearchForm />);

    await userEvent.type(screen.getByLabelText(/research question/i), "sleep");
    await userEvent.click(screen.getByRole("button", { name: /start research/i }));

    expect(await screen.findByText(/enter a specific research question/i)).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("creates a run and navigates to its detail page", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ researchRunId: "11111111-1111-4111-8111-111111111111", status: "Queued" }), {
        status: 201,
        headers: { "Content-Type": "application/json" }
      })
    );
    renderWithClient(<CreateResearchForm />);

    await userEvent.type(screen.getByLabelText(/research question/i), "Does sleep deprivation impair memory?");
    await userEvent.click(screen.getByRole("button", { name: /start research/i }));

    expect(await screen.findByRole("button", { name: /start research/i })).toBeEnabled();
    expect(fetchMock).toHaveBeenCalled();
    expect(push).toHaveBeenCalledWith("/research/11111111-1111-4111-8111-111111111111");
  });

  it("renders backend failures", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ title: "Bad request", detail: "Question is required", status: 400 }), {
        status: 400,
        headers: { "Content-Type": "application/problem+json" }
      })
    );
    renderWithClient(<CreateResearchForm />);

    await userEvent.type(screen.getByLabelText(/research question/i), "Does sleep deprivation impair memory?");
    await userEvent.click(screen.getByRole("button", { name: /start research/i }));

    expect(await screen.findByText("Question is required")).toBeInTheDocument();
  });

  it("reuses the same key after an ambiguous response and creates a new key for a changed question", async () => {
    fetchMock.mockRejectedValue(new TypeError("Lost response"));
    renderWithClient(<CreateResearchForm />);
    await userEvent.type(screen.getByLabelText(/research question/i), "Does sleep deprivation impair memory?");
    await userEvent.click(screen.getByRole("button", { name: /start research/i }));
    await waitFor(() => expect(screen.getByRole("button", { name: /start research/i })).toBeEnabled());
    await userEvent.click(screen.getByRole("button", { name: /start research/i }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    const key = new Headers(fetchMock.mock.calls[0][1]?.headers).get("idempotency-key");
    expect(key).toMatch(/^[a-f\d-]{36}$/i);
    expect(new Headers(fetchMock.mock.calls[1][1]?.headers).get("idempotency-key")).toBe(key);
    await waitFor(() => expect(screen.getByRole("button", { name: /start research/i })).toBeEnabled());
    await userEvent.type(screen.getByLabelText(/research question/i), " in adults");
    await userEvent.click(screen.getByRole("button", { name: /start research/i }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3));
    expect(new Headers(fetchMock.mock.calls[2][1]?.headers).get("idempotency-key")).not.toBe(key);
    expect(JSON.parse(fetchMock.mock.calls[0][1]?.body as string)).toEqual({ question: "Does sleep deprivation impair memory?" });
  });

  it("blocks duplicate form submits while transport is pending", async () => {
    let resolve!: (response: Response) => void;
    fetchMock.mockImplementation(() => new Promise<Response>(done => { resolve = done; }));
    renderWithClient(<CreateResearchForm />);
    const input = screen.getByLabelText(/research question/i);
    await userEvent.type(input, "A sufficiently specific question");
    fireEvent.submit(input.closest("form")!); fireEvent.submit(input.closest("form")!);
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(screen.getByRole("button", { name: /start research/i })).toBeDisabled();
    resolve(Response.json({ researchRunId: "11111111-1111-4111-8111-111111111111", status: "Queued" }, { status: 201 }));
    await waitFor(() => expect(push).toHaveBeenCalled());
  });

  it.each([[429, "Your outstanding research limit has been reached"], [429, "Research capacity is currently full"],
    [429, "Your daily research limit has been reached"], [503, "New research submissions are temporarily paused"],
    [409, "This submission key was already used for a different question"]])("renders admission HTTP %s without scientific-failure navigation", async (status, title) => {
    fetchMock.mockResolvedValue(Response.json({ title, status }, { status: Number(status) }));
    renderWithClient(<CreateResearchForm />);
    await userEvent.type(screen.getByLabelText(/research question/i), "A sufficiently specific question");
    await userEvent.click(screen.getByRole("button", { name: /start research/i }));
    expect(await screen.findByText(String(title))).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });
});

function renderWithClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}
