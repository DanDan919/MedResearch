import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
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
});

function renderWithClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}
