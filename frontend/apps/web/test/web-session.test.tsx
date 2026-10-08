import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useEffect } from "react";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { AppProviders } from "../components/app-providers";
import { LoginForm } from "../components/login-form";
import { useWebSession } from "../components/web-session-provider";
import type { PublicSession } from "../lib/auth/types";

vi.mock("next/navigation", () => ({ usePathname: () => "/login" }));
const a: PublicSession = { authenticated: true, mode: "oidc", sessionId: "session-A", displayName: "User A", expiresAt: Date.now() + 300_000 };
const b: PublicSession = { ...a, sessionId: "session-B", displayName: "User B" };
const signedOut: PublicSession = { ...a, authenticated: false, sessionId: null, displayName: null, expiresAt: null };
const keys = ["history", "run-details", "report", "provenance", "quantitative"].map(name => [name, "run-A"]);
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
beforeEach(() => {
  vi.stubGlobal("matchMedia", vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() })));
});

function Probe({ capture }: { capture: (client: QueryClient) => void }) {
  const client = useQueryClient(); const { session } = useWebSession();
  useEffect(() => { capture(client); }, [capture, client]);
  return <div>{session.displayName ?? "Signed out"}</div>;
}

describe("browser session cache boundary", () => {
  it("new opaque session discards every old scientific cache, not only history", async () => {
    let current = a; const clients: QueryClient[] = [];
    const capture = (client: QueryClient) => { if (!clients.includes(client)) clients.push(client); };
    vi.stubGlobal("fetch", vi.fn(async () => Response.json(current)));
    render(<AppProviders initialSession={a}><Probe capture={capture} /></AppProviders>);
    await screen.findByText("User A");
    for (const key of keys) clients[0].setQueryData(key, { private: "User A data" });
    expect(clients[0].getQueryCache().getAll()).toHaveLength(5);
    current = b;
    await act(async () => { window.dispatchEvent(new Event("focus")); });
    await screen.findByText("User B");
    await waitFor(() => expect(clients).toHaveLength(2));
    expect(clients[0].getQueryCache().getAll()).toHaveLength(0);
    for (const key of keys) expect(clients[1].getQueryData(key)).toBeUndefined();
  });
  it("API 401 event clears scientific data and public identity", async () => {
    const clients: QueryClient[] = []; const capture = (client: QueryClient) => { clients.push(client); };
    let current = a;
    vi.stubGlobal("fetch", vi.fn(async () => Response.json(current)));
    render(<AppProviders initialSession={a}><Probe capture={capture} /></AppProviders>);
    await screen.findByText("User A"); for (const key of keys) clients[0].setQueryData(key, "private");
    current = signedOut;
    await act(async () => { window.dispatchEvent(new Event("medresearch-session-ended")); });
    await screen.findByText("Signed out"); expect(clients[0].getQueryCache().getAll()).toHaveLength(0);
  });
  it("login accurately distinguishes unavailable, signed-out and provider error", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => Response.json(signedOut)));
    const tree = render(<AppProviders initialSession={signedOut}><LoginForm configured={false} returnTo="/" reason={null} /></AppProviders>);
    expect(screen.getByRole("alert")).toHaveTextContent("Authentication is not configured");
    tree.rerender(<AppProviders initialSession={signedOut}><LoginForm configured returnTo="/" reason="authentication-failed" /></AppProviders>);
    expect(screen.getByRole("status")).toHaveTextContent("Sign-in failed");
    expect(screen.getByRole("button", { name: "Sign in" })).toBeEnabled();
  });
  it("failed login fetch does not manufacture authenticated success", async () => {
    vi.stubGlobal("fetch", vi.fn(async input => String(input).includes("/login") ? new Response(null, { status: 503 }) : Response.json(signedOut)));
    render(<AppProviders initialSession={signedOut}><LoginForm configured returnTo="/" reason={null} /></AppProviders>);
    await act(async () => { screen.getByRole("button", { name: "Sign in" }).click(); });
    await expect(screen.findByRole("alert")).resolves.toHaveTextContent("Identity service unavailable");
    expect(screen.queryByRole("link", { name: "Continue to workspace" })).toBeNull();
  });
});
