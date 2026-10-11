"use client";

import { useEffect, useRef } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { RefreshCw, RotateCcw } from "lucide-react";
import { queryKeys } from "@medresearch/api";
import { Button } from "@medresearch/ui";
import { useApiClient } from "../../lib/api";

export function SynthesisRetry({ researchRunId }: { researchRunId: string }) {
  const client = useApiClient();
  const cache = useQueryClient();
  const key = useRef<string | null>(null);
  const submitting = useRef(false);
  const query = useQuery({
    queryKey: queryKeys.research.synthesisRetry(researchRunId),
    queryFn: ({ signal }) => client.getSynthesisRetry(researchRunId, signal),
    refetchInterval: current => current.state.data?.availability === "Active" ? 5000 : false
  });
  const mutation = useMutation({
    mutationFn: (idempotencyKey: string) => client.retrySynthesis(researchRunId, idempotencyKey),
    onSuccess: state => {
      cache.setQueryData(queryKeys.research.synthesisRetry(researchRunId), state);
      void cache.invalidateQueries({ queryKey: queryKeys.research.detail(researchRunId) });
      void cache.invalidateQueries({ queryKey: queryKeys.research.list() });
    },
    onSettled: () => { submitting.current = false; }
  });
  useEffect(() => {
    if (query.data?.attemptStatus === "Succeeded") {
      void cache.invalidateQueries({ queryKey: queryKeys.research.progress(researchRunId) });
      void cache.invalidateQueries({ queryKey: queryKeys.research.report(researchRunId) });
    }
  }, [cache, researchRunId, query.data?.attemptStatus]);

  const state = query.data;
  const labels = { Queued: "Report retry queued", Running: "Report retry running", Succeeded: "Report retry succeeded", Failed: "Report retry failed", OutcomeUnknown: "Generation outcome unknown. Operator review required." };
  return (
    <section className="space-y-3 border-y py-4" aria-label="Report recovery">
      <p className="text-sm font-medium" role="status">{query.isError ? "Retry availability could not be verified." : !state ? "Checking report recovery" :
        state.attemptStatus ? labels[state.attemptStatus] : state.availability === "Available" ? "Report retry available" : "Report retry not available"}</p>
      <p className="text-sm text-muted-foreground">Previous evidence remains available. Only report generation is retried. {state ? `Up to ${state.generationBudget} new LLM generation calls, including repairs, may incur additional cost.` : "New LLM generation calls may incur additional cost."} One retry per run.</p>
      {state?.availability === "Available" && !query.isError ? (
        <Button disabled={mutation.isPending} onClick={() => {
          if (submitting.current) return;
          submitting.current = true;
          key.current ??= crypto.randomUUID();
          mutation.mutate(key.current);
        }}><RotateCcw className="h-4 w-4" />Retry report generation</Button>
      ) : null}
      {state?.availability === "Exhausted" ? <p className="text-sm text-muted-foreground">Retry budget exhausted.</p> : null}
      {mutation.isError ? <p className="text-sm text-destructive" role="alert">The request could not be confirmed. Refresh recovery status or resend the same request.</p> : null}
      <Button variant="ghost" size="sm" onClick={() => { void query.refetch(); }}><RefreshCw className="h-4 w-4" />Refresh recovery status</Button>
    </section>
  );
}
