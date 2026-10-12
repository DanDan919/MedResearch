"use client";

import Link from "next/link";
import { useRef, useState } from "react";
import { ArrowLeft, BookOpen, Check, Circle, ExternalLink, Loader2, RefreshCw, X } from "lucide-react";
import { Badge, Button, cn, Dialog } from "@medresearch/ui";
import { MedResearchApiError, safeQuickPublicationUrl, type QuickResearchDetails, type QuickPublication } from "@medresearch/api";
import { useQuickResearch } from "../../lib/quick-research";
import { useWebSession } from "../web-session-provider";

export function QuickResultWorkspace({ runId }: { runId: string }) {
  const query = useQuickResearch(runId);
  const { session } = useWebSession();
  if (!session.authenticated) return <QuickNotice title="Sign in required" message="This result is private." />;
  if (query.isPending) return <div role="status" className="flex items-center gap-2 py-8 text-sm"><Loader2 className="h-4 w-4 animate-spin" />Loading Quick result</div>;
  if (query.isError || !query.data) return <QuickError error={query.error} refresh={() => void query.refetch()} />;
  return <QuickResult key={`${session.sessionId}:${query.data.id}`} details={query.data} refresh={() => void query.refetch()} refreshing={query.isFetching} />;
}

export function QuickResult({ details, refresh, refreshing = false }: { details: QuickResearchDetails; refresh: () => void; refreshing?: boolean }) {
  const [selected, setSelected] = useState<{ id: string; excerpt?: string } | null>(null);
  const sourceTrigger = useRef<HTMLButtonElement | null>(null);
  const sources = details.retrieval?.sources ?? [];
  const selectedSource = sources.find(source => source.id === selected?.id);
  const answer = details.answer;
  const active = details.status === "Queued" || details.status === "Processing";
  const allExcerpts = answer ? [...answer.findings.flatMap(f => f.citations), ...answer.contradictions.flatMap(c => c.citations)]
    .filter(c => c.sourceId === selected?.id).map(c => c.excerpt) : [];
  const excerpts = selected?.excerpt ? [selected.excerpt] : [...new Set(allExcerpts)];
  const refs = (citations: { sourceId: string; excerpt: string }[]) => <span className="ml-2 inline-flex flex-wrap gap-2">
    {citations.map(citation => {
      const ordinal = sources.findIndex(s => s.id === citation.sourceId) + 1;
      return <button key={citation.sourceId} type="button" onClick={event => { sourceTrigger.current = event.currentTarget; setSelected({ id: citation.sourceId, excerpt: citation.excerpt }); }}
        className="min-h-9 min-w-9 text-sm font-medium text-primary underline underline-offset-4 focus-visible:outline-2 focus-visible:outline-ring"
        aria-label={`View source ${ordinal} and supporting excerpt`}>[{ordinal}]</button>;
    })}
  </span>;
  return <div className="mx-auto max-w-4xl space-y-6" data-testid="quick-result">
    <div className="flex items-center justify-between gap-3">
      <Link href="/quick-research" className="inline-flex min-h-9 items-center gap-2 text-sm text-muted-foreground"><ArrowLeft className="h-4 w-4" />Quick Research</Link>
      <Button variant="ghost" size="sm" onClick={refresh} disabled={refreshing} aria-label="Refresh result" title="Refresh result"><RefreshCw className={cn("h-4 w-4", refreshing && "animate-spin")} /></Button>
    </div>
    <header className="space-y-3 border-b border-border pb-5">
      <Badge tone={details.status === "Failed" ? "warning" : details.status === "Completed" ? "success" : "info"}>{details.status}</Badge>
      <h1 className="max-w-full break-words text-2xl font-semibold leading-snug" dir="auto">{details.question}</h1>
    </header>
    {active && <QuickProgress details={details} />}
    {details.status === "Failed" && <QuickNotice title="Answer unavailable" message={failureMessage(details.safeError)} alert />}
    {answer ? <>
      <section aria-labelledby="quick-answer-heading" className="space-y-3">
        <h2 id="quick-answer-heading" className="text-sm font-semibold text-muted-foreground">{answer.uncertainty === "Insufficient" ? "Insufficient evidence" : "Short answer"}</h2>
        <p className="whitespace-pre-line text-lg leading-relaxed" dir="auto">{answer.shortAnswer}</p>
        <p className="text-sm text-warning">Uncertainty: {answer.uncertainty === "VeryLimited" ? "Very limited support" : answer.uncertainty === "Limited" ? "Limited support" : "Not enough information"}</p>
      </section>
      {answer.findings.length > 0 && <section aria-labelledby="quick-findings-heading" className="space-y-3 border-t border-border pt-5">
        <h2 id="quick-findings-heading" className="text-base font-semibold">Findings</h2>
        <ul className="space-y-3">{answer.findings.map((finding, index) => <li key={index} className="text-sm leading-6"><span dir="auto">{finding.text}</span>{refs(finding.citations)}</li>)}</ul>
      </section>}
      {answer.contradictions.length > 0 && <section aria-labelledby="quick-disagreements-heading" className="space-y-3 border-t border-border pt-5">
        <h2 id="quick-disagreements-heading" className="text-base font-semibold">Where findings differ</h2>
        <ul className="space-y-3">{answer.contradictions.map((finding, index) => <li key={index} className="text-sm leading-6"><span dir="auto">{finding.text}</span>{refs(finding.citations)}</li>)}</ul>
      </section>}
      <section aria-labelledby="quick-limitations-heading" className="space-y-3 border-t border-border pt-5">
        <h2 id="quick-limitations-heading" className="text-base font-semibold">Limitations</h2>
        <ul className="list-disc space-y-2 pl-5 text-sm leading-6 text-muted-foreground">{answer.limitations.map((limitation, index) => <li key={index} dir="auto">{limitation}</li>)}</ul>
      </section>
    </> : !active && details.status !== "Failed" && <QuickNotice title="No answer available" message={noAnswerMessage(details)} />}
    {details.retrieval?.outcome === "Partial" && <p role="status" className="border-l-2 border-warning pl-3 text-sm leading-6">Some sources were unavailable. Search coverage is incomplete.</p>}
    <section aria-labelledby="quick-sources-heading" className="space-y-3 border-t border-border pt-5">
      <h2 id="quick-sources-heading" className="text-base font-semibold">Scientific sources</h2>
      {sources.length === 0 ? <p className="text-sm text-muted-foreground">{active ? "No publications selected yet." : "No scientific sources were selected."}</p>
        : <ol className="divide-y divide-border">{sources.map((source, index) => <li key={source.id} className="flex min-w-0 items-start gap-3 py-4">
          <span className="shrink-0 pt-1 text-xs text-muted-foreground">[{index + 1}]</span>
          <div className="min-w-0 flex-1">
            <button type="button" className="text-left text-sm font-medium leading-6 underline decoration-border underline-offset-4 [overflow-wrap:anywhere] hover:text-primary focus-visible:outline-2 focus-visible:outline-ring"
              onClick={event => { sourceTrigger.current = event.currentTarget; setSelected({ id: source.id }); }}>{source.title}</button>
            <p className="mt-1 text-xs text-muted-foreground">{publicationInfo(source)}</p>
          </div>
          <BookOpen aria-hidden className="mt-1 h-4 w-4 shrink-0 text-muted-foreground" />
        </li>)}</ol>}
    </section>
    <details className="border-t border-border pt-4 text-xs text-muted-foreground">
      <summary className="min-h-8 cursor-pointer font-medium">Technical details</summary>
      <dl className="mt-3 space-y-2"><div><dt className="inline font-medium">Run: </dt><dd className="inline break-all">{details.id}</dd></div>
        <div><dt className="inline font-medium">Answer status: </dt><dd className="inline">{details.answerStatus ?? "Not available"}</dd></div></dl>
      {details.retrieval && <ul className="mt-3 space-y-2">{details.retrieval.attempts.map(attempt => <li key={attempt.id}>
        {attempt.provider}: {attemptLabel(attempt.outcome)}{attempt.returnedCount !== null ? ` (${attempt.returnedCount})` : ""}
      </li>)}</ul>}
    </details>
    <p className="border-t border-border pt-4 text-xs text-muted-foreground">Research information, not a diagnosis or individual treatment advice.</p>
    <Dialog.Root open={Boolean(selectedSource)} onOpenChange={open => { if (!open) setSelected(null); }}>
      <Dialog.Portal><Dialog.Overlay className="fixed inset-0 z-40 bg-black/40" />
        <Dialog.Content onCloseAutoFocus={event => { event.preventDefault(); if (sourceTrigger.current?.isConnected) sourceTrigger.current.focus(); }}
          className="fixed left-1/2 top-1/2 z-50 max-h-[85dvh] w-[calc(100%_-_2rem)] max-w-2xl -translate-x-1/2 -translate-y-1/2 overflow-y-auto rounded-lg border border-border bg-surface p-5 [overflow-wrap:anywhere] focus:outline-none sm:p-6">
          <Dialog.Close asChild><Button variant="ghost" size="sm" className="absolute right-2 top-2" aria-label="Close source" title="Close source"><X className="h-4 w-4" /></Button></Dialog.Close>
          <Dialog.Title className="pr-10 text-lg font-semibold leading-7">{selectedSource?.title}</Dialog.Title>
          <Dialog.Description className="mt-2 text-sm text-muted-foreground">{selectedSource ? publicationInfo(selectedSource) : ""}</Dialog.Description>
          {selectedSource && <SourceDetails source={selectedSource} excerpts={excerpts} />}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  </div>;
}

function SourceDetails({ source, excerpts }: { source: QuickPublication; excerpts: string[] }) {
  const url = safeQuickPublicationUrl(source);
  return <div className="mt-5 space-y-5 text-sm">
    {source.authors.length > 0 && <p className="leading-6 text-muted-foreground">{source.authors.join(", ")}</p>}
    <dl className="space-y-2">{[["PMID", source.pmid], ["PMCID", source.pmcid], ["DOI", source.doi]].map(([label, value]) => value ? <div key={label}><dt className="inline font-medium">{label}: </dt><dd className="inline break-all">{value}</dd></div> : null)}</dl>
    {source.metadataConflicts.length > 0 && <p className="text-warning">Some publication metadata differs between sources.</p>}
    {url ? <a href={url} target="_blank" rel="noopener noreferrer" referrerPolicy="no-referrer" className="inline-flex min-h-9 items-center gap-2 text-primary underline"><ExternalLink className="h-4 w-4" />Open publication</a>
      : <p className="text-muted-foreground">Publication link unavailable.</p>}
    <section className="space-y-3 border-t border-border pt-4">
      <h3 className="font-semibold">Supporting excerpt{excerpts.length > 1 ? "s" : ""}</h3>
      {excerpts.length > 0 ? excerpts.map((excerpt, index) => <blockquote key={index} className="whitespace-pre-line border-l-2 border-primary pl-4 leading-7" dir="auto">{excerpt}</blockquote>)
        : <p className="text-muted-foreground">No supporting excerpt is available for this publication.</p>}
    </section>
  </div>;
}

function QuickProgress({ details }: { details: QuickResearchDetails }) {
  const found = details.retrieval?.isFinal === true;
  const phases = ["Finding studies", "Comparing findings", "Preparing answer"];
  return <ol aria-label="Quick research progress" className="grid gap-3 border-b border-border pb-5 sm:grid-cols-3">
    {phases.map((label, index) => {
      const done = index === 0 && found;
      const current = details.status === "Processing" && (found ? index > 0 : index === 0);
      const Icon = done ? Check : current ? Loader2 : Circle;
      return <li key={label} className="flex items-center gap-2 text-sm" aria-current={current ? "step" : undefined}>
        <Icon className={cn("h-4 w-4 shrink-0", current && "animate-spin text-primary", done && "text-success")} />
        <span>{label}</span><span className="sr-only">{done ? ": complete" : current ? ": in progress" : ": pending"}</span>
      </li>;
    })}
  </ol>;
}

export function QuickNotice({ title, message, alert = false }: { title: string; message: string; alert?: boolean }) {
  return <section role={alert ? "alert" : "status"} className="space-y-2 border-l-2 border-warning py-1 pl-4"><h2 className="text-base font-semibold">{title}</h2><p className="text-sm leading-6 text-muted-foreground">{message}</p></section>;
}
export function QuickError({ error, refresh }: { error: unknown; refresh: () => void }) {
  const missing = error instanceof MedResearchApiError && error.kind === "not-found";
  return <div className="mx-auto max-w-4xl space-y-4"><Link href="/quick-research" className="text-sm text-primary underline">Quick Research</Link>
    <QuickNotice title={missing ? "Result not available" : "Could not load Quick Research"}
      message={missing ? "This result is not available to your account." : "The service could not return this result."} alert />
    {!missing && <Button variant="secondary" size="sm" onClick={refresh}><RefreshCw className="h-4 w-4" />Refresh</Button>}
  </div>;
}
export function publicationInfo(source: QuickPublication): string {
  const date = source.publicationDate ? new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" }).format(new Date(source.publicationDate))
    : source.publicationYear?.toString();
  return [source.journal, date].filter(Boolean).join(" · ") || "Publication details not reported.";
}
function failureMessage(error: string | null) {
  return error === "QuickRetrievalFailed" ? "The source search could not be completed. No answer was generated."
    : error === "QuickAnswerOutcomeUnknown" ? "Processing ended without a confirmed answer."
    : "An answer could not be completed. Any selected sources are retained below.";
}
function noAnswerMessage(details: QuickResearchDetails) {
  return details.retrieval?.outcome === "NeedsClarification" ? "The question could not be searched without guessing its meaning."
    : details.retrieval?.outcome === "ZeroResults" ? "The bounded search returned no publications. This does not establish that no research exists."
    : "No source-grounded answer was saved for this run.";
}
function attemptLabel(outcome: string) {
  const labels: Record<string, string> = { Started: "In progress", SucceededWithResults: "Results returned", SucceededZeroResults: "No results returned",
    Failed: "Unavailable", TimedOut: "Timed out", OutcomeUnknown: "Outcome not confirmed", Disabled: "Not enabled" };
  return Object.hasOwn(labels, outcome) ? labels[outcome] : "Not available";
}
