"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { ArrowLeft, Loader2, Send } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { Button, Textarea } from "@medresearch/ui";
import { MedResearchApiError, quickQueryKeys } from "@medresearch/api";
import { useApiClient } from "../../lib/api";
import { useWebSession } from "../web-session-provider";
import { QuickNotice } from "./quick-result-workspace";

export function QuickCreate() {
  const { session } = useWebSession();
  if (!session.authenticated) return <QuickNotice title="Sign in required" message="Your question and results are private." />;
  return <QuickCreateForm key={session.sessionId} sessionId={session.sessionId} />;
}

function QuickCreateForm({ sessionId }: { sessionId: string | null }) {
  const api = useApiClient();
  const router = useRouter();
  const cache = useQueryClient();
  const [question, setQuestion] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [unknown, setUnknown] = useState(false);
  const inFlight = useRef(false);
  const active = useRef(true);
  const controller = useRef<AbortController | null>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); }; }, []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (inFlight.current || unknown) return;
    const trimmed = question.trim();
    if (!trimmed || trimmed.length > 2000) { setError("Enter a question of up to 2000 characters."); return; }
    inFlight.current = true; setPending(true); setError(null);
    controller.current = new AbortController();
    try {
      const result = await api.createQuickResearch(trimmed, controller.current.signal);
      if (!active.current) return;
      cache.setQueryData(quickQueryKeys.detail(sessionId, result.id), result);
      void cache.invalidateQueries({ queryKey: ["quick-research", sessionId, "history"] });
      router.push(`/quick-research/${result.id}`);
    } catch (failure) {
      if (!active.current) return;
      const rejected = failure instanceof MedResearchApiError && failure.status !== undefined && failure.status >= 400 && failure.status < 500;
      if (rejected) setError(failure.status === 401 ? "Sign in to ask a question." : "The question was not accepted. Check it and try again.");
      else { setUnknown(true); setError("Could not confirm whether your question was saved. Check your history before submitting again."); }
    } finally { if (active.current) { setPending(false); inFlight.current = false; } }
  }

  return <div className="mx-auto max-w-3xl space-y-6">
    <Link href="/quick-research" className="inline-flex min-h-9 items-center gap-2 text-sm text-muted-foreground"><ArrowLeft className="h-4 w-4" />Quick Research</Link>
    <h1 className="text-2xl font-semibold">Ask a medical question</h1>
    <form onSubmit={submit} className="space-y-4">
      <label htmlFor="quick-question" className="block text-sm font-medium">Your question</label>
      <Textarea id="quick-question" value={question} onChange={event => setQuestion(event.target.value)} rows={5} maxLength={2000}
        disabled={pending || unknown} required aria-describedby={error ? "quick-create-error" : undefined} />
      {error && <p id="quick-create-error" role="alert" className="text-sm leading-6 text-warning">{error}</p>}
      <div className="flex flex-wrap items-center gap-4"><Button type="submit" disabled={pending || unknown}>
        {pending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}Ask question
      </Button>{unknown && <Link href="/quick-research" className="text-sm text-primary underline">Check history</Link>}</div>
    </form>
    <p className="border-t border-border pt-4 text-xs leading-6 text-muted-foreground">Research information, not a diagnosis or individual treatment advice.</p>
  </div>;
}
