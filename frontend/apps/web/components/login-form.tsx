"use client";

import { useState } from "react";
import Link from "next/link";
import { LogIn } from "lucide-react";
import { Button } from "@medresearch/ui";
import { useWebSession } from "./web-session-provider";

const messages: Record<string, string> = {
  "authentication-failed": "Sign-in failed. Please try again.",
  "provider-unavailable": "Identity service unavailable. Please try again later.",
  "session-expired": "Your session ended. Sign in again.",
  "signed-out": "Signed out."
};

export function LoginForm({ configured, returnTo, reason }: { configured: boolean; returnTo: string; reason: string | null }) {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { session, endSession } = useWebSession();
  return <main className="mx-auto flex min-h-screen max-w-md flex-col justify-center gap-5 px-6">
    <div className="text-xl font-semibold">MedResearch</div>
    <h1 className="text-2xl font-semibold">Sign in</h1>
    {!configured ? <p role="alert" className="text-sm text-muted-foreground">Authentication is not configured. Contact the operator.</p> :
      <form action={`/api/auth/login?returnTo=${encodeURIComponent(returnTo)}`} method="post" onSubmit={async event => {
        event.preventDefault(); setSubmitting(true); setError(null);
        try {
          const response = await fetch(`/api/auth/login?returnTo=${encodeURIComponent(returnTo)}`, {
            method: "POST", headers: { Accept: "application/json" }, credentials: "same-origin", cache: "no-store"
          });
          if (!response.ok) throw new Error("Sign-in unavailable");
          const result = await response.json() as { authorizationUrl?: unknown };
          if (typeof result.authorizationUrl !== "string" || new URL(result.authorizationUrl).protocol !== "https:") throw new Error("Sign-in unavailable");
          endSession(); window.location.assign(result.authorizationUrl);
        } catch { setError("Identity service unavailable. Please try again later."); setSubmitting(false); }
      }}>
        <Button type="submit" disabled={submitting} className="w-full"><LogIn className="mr-2 h-4 w-4" />{submitting ? "Signing in..." : session.authenticated ? "Switch account" : "Sign in"}</Button>
      </form>}
    {reason && messages[reason] && <p role="status" className="text-sm">{messages[reason]}</p>}
    {error && <p role="alert" className="text-sm">{error}</p>}
    {session.authenticated && <Link href={returnTo} className="text-sm underline">Continue to workspace</Link>}
  </main>;
}
