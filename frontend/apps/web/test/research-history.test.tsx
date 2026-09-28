import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ResearchHistory } from "../components/research/research-history";

const push = vi.fn();
const fetchMock = vi.fn<typeof fetch>();
let currentSearch = "";

vi.mock("next/navigation", () => ({
  usePathname: () => "/research",
  useRouter: () => ({ push }),
  useSearchParams: () => new URLSearchParams(currentSearch)
}));

describe("ResearchHistory", () => {
  beforeEach(() => {
    push.mockReset();
    fetchMock.mockReset();
    currentSearch = "";
    vi.stubGlobal("fetch", fetchMock);
  });

  it("renders an empty history state", async () => {
    fetchMock.mockResolvedValue(jsonResponse({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }));

    renderWithClient(<ResearchHistory />);

    expect(await screen.findByText("No research yet")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      "http://localhost:8080/api/research?page=1&pageSize=20",
      expect.objectContaining({ method: "GET" })
    );
  });

  it("renders paged history rows and opens a run", async () => {
    fetchMock.mockResolvedValue(
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
        page: 1,
        pageSize: 20,
        totalCount: 21,
        totalPages: 2
      })
    );

    renderWithClient(<ResearchHistory />);

    expect(await screen.findByText("Does sleep deprivation impair memory?")).toBeInTheDocument();
    expect(screen.getAllByText("Completed")).toHaveLength(2);
    expect(screen.getByRole("link", { name: /open/i })).toHaveAttribute(
      "href",
      "/research/11111111-1111-4111-8111-111111111111"
    );

    await userEvent.click(screen.getByRole("button", { name: /next/i }));

    expect(push).toHaveBeenCalledWith("/research?page=2");
  });

  it("applies status filters through the URL", async () => {
    currentSearch = "status=Failed&page=3";
    fetchMock.mockResolvedValue(jsonResponse({ items: [], page: 3, pageSize: 20, totalCount: 0, totalPages: 0 }));

    renderWithClient(<ResearchHistory />);

    expect(await screen.findByText("No research matches this status")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      "http://localhost:8080/api/research?page=3&pageSize=20&status=Failed",
      expect.objectContaining({ method: "GET" })
    );

    await userEvent.selectOptions(screen.getByLabelText("Status"), "Completed");

    expect(push).toHaveBeenCalledWith("/research?status=Completed");
  });

  it("renders backend failures", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ title: "Unavailable", detail: "Database is unavailable", status: 503 }), {
        status: 503,
        headers: { "Content-Type": "application/problem+json" }
      })
    );

    renderWithClient(<ResearchHistory />);

    expect(await screen.findByText("Research history unavailable")).toBeInTheDocument();
  });
});

function renderWithClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
}
