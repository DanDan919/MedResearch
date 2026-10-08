"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { usePathname } from "next/navigation";
import { z } from "zod";
import { AppThemeProvider } from "./app-theme-provider";
import { WebSessionContext } from "./web-session-provider";
import type { PublicSession } from "../lib/auth/types";

const sessionSchema = z.object({ authenticated: z.boolean(), mode: z.enum(["oidc", "development-local", "unavailable"]),
  sessionId: z.string().nullable(), displayName: z.string().max(100).nullable(), expiresAt: z.number().finite().nullable() }).strict()
  .refine(value => !value.authenticated || (value.sessionId !== null && value.mode !== "unavailable"));

export function AppProviders({ children, initialSession }: { children: React.ReactNode; initialSession: PublicSession }) {
  const [session, setSession] = useState(initialSession);
  const [unavailable, setUnavailable] = useState(false);
  const pathname = usePathname();
  const channel = useRef<BroadcastChannel | null>(null);
  // A new opaque session ID intentionally creates a new, empty scientific query cache.
  const queryClient = useMemo(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            meta: { sessionId: session.sessionId },
            staleTime: 10_000,
            retry: 1,
            refetchOnWindowFocus: false
          },
          mutations: {
            retry: false
          }
        }
      }), [session.sessionId]
  );

  const clearPrivateState = useCallback(() => {
    void queryClient.cancelQueries();
    queryClient.clear();
    setSession(value => ({ ...value, authenticated: false, sessionId: null, displayName: null, expiresAt: null }));
  }, [queryClient]);
  const endSession = useCallback(() => {
    clearPrivateState();
    channel.current?.postMessage("session-ended");
  }, [clearPrivateState]);

  useEffect(() => () => { void queryClient.cancelQueries(); queryClient.clear(); }, [queryClient]);
  useEffect(() => {
    if (typeof BroadcastChannel !== "undefined") {
      const broadcast = new BroadcastChannel("medresearch-session");
      channel.current = broadcast;
      broadcast.onmessage = event => { if (event.data === "session-ended") clearPrivateState(); };
      return () => { broadcast.close(); channel.current = null; };
    }
  }, [clearPrivateState]);

  useEffect(() => {
    const controller = new AbortController();
    let checking = false;
    const check = async () => {
      if (checking || controller.signal.aborted) return;
      checking = true;
      try {
        const response = await fetch("/api/auth/session", { cache: "no-store", credentials: "same-origin", signal: controller.signal });
        if (controller.signal.aborted) return;
        if (!response.ok) { setUnavailable(true); return; }
        const current = sessionSchema.parse(await response.json());
        if (controller.signal.aborted) return;
        setUnavailable(false);
        if (!current.authenticated) clearPrivateState();
        setSession(current);
      } catch { if (!controller.signal.aborted) setUnavailable(true); }
      finally { checking = false; }
    };
    const ended = () => { endSession(); };
    const restored = (event: PageTransitionEvent) => {
      if (event.persisted) { setUnavailable(true); void check(); }
    };
    const focus = () => { void check(); };
    void check();
    const timer = window.setInterval(() => { void check(); }, 20_000);
    window.addEventListener("focus", focus);
    window.addEventListener("pageshow", restored);
    window.addEventListener("medresearch-session-ended", ended);
    return () => {
      controller.abort(); window.clearInterval(timer);
      window.removeEventListener("focus", focus); window.removeEventListener("pageshow", restored);
      window.removeEventListener("medresearch-session-ended", ended);
    };
  }, [clearPrivateState, endSession]);

  useEffect(() => {
    if (!session.authenticated || session.expiresAt === null) return;
    const timer = window.setTimeout(endSession, Math.max(0, session.expiresAt - Date.now()));
    return () => window.clearTimeout(timer);
  }, [session.authenticated, session.expiresAt, endSession]);

  useEffect(() => {
    if (!session.authenticated && pathname !== "/login") {
      window.location.replace(`/login?reason=session-expired&returnTo=${encodeURIComponent(window.location.pathname + window.location.search)}`);
    }
  }, [session.authenticated, pathname]);

  return (
    <AppThemeProvider defaultTheme="system" enableSystem>
      <WebSessionContext.Provider value={{ session, endSession }}>
        <QueryClientProvider client={queryClient}>
          {pathname === "/login" || (session.authenticated && !unavailable) ? children : <div role="alert" className="p-6 text-sm">{unavailable ? "Session service unavailable. Reload to retry." : "Sign in required."}</div>}
        </QueryClientProvider>
      </WebSessionContext.Provider>
    </AppThemeProvider>
  );
}
