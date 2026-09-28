import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiStatusIndicator } from "../components/api-status-indicator";

const fetchMock = vi.fn<typeof fetch>();

describe("ApiStatusIndicator", () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
  });

  it("shows connected when readiness succeeds", async () => {
    fetchMock.mockResolvedValue(new Response("Healthy", { status: 200 }));
    renderWithClient(<ApiStatusIndicator />);

    expect(await screen.findByText("API Connected")).toBeInTheDocument();
  });

  it("shows unavailable when readiness fails", async () => {
    fetchMock.mockResolvedValue(new Response("Unhealthy", { status: 503 }));
    renderWithClient(<ApiStatusIndicator />);

    expect(await screen.findByText("API Unavailable")).toBeInTheDocument();
  });
});

function renderWithClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}
