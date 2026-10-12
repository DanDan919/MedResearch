"use client";

import Link from "next/link";
import { useState } from "react";
import { ArrowLeft, ArrowRight, BookOpen, Loader2, RefreshCw } from "lucide-react";
import { Badge, Button } from "@medresearch/ui";
import { useQuickHistory } from "../../lib/quick-research";
import { QuickError, QuickNotice } from "./quick-result-workspace";
import { useWebSession } from "../web-session-provider";

export function QuickHistory() {
  const [page, setPage] = useState(1);
  const { session } = useWebSession();
  const query = useQuickHistory(page);
  if (!session.authenticated) return <QuickNotice title="Sign in required" message="Your results are private." />;
  if (query.isPending) return <div role="status" className="flex items-center gap-2 py-8 text-sm"><Loader2 className="h-4 w-4 animate-spin" />Loading Quick Research</div>;
  if (query.isError || !query.data) return <QuickError error={query.error} refresh={() => void query.refetch()} />;
  const history = query.data;
  return <div className="mx-auto max-w-4xl space-y-5">
    <header className="flex items-center justify-between gap-3 border-b border-border pb-5"><h1 className="text-2xl font-semibold">Quick Research</h1>
      <Button variant="ghost" size="sm" onClick={() => void query.refetch()} disabled={query.isFetching} aria-label="Refresh results" title="Refresh results"><RefreshCw className="h-4 w-4" /></Button></header>
    {history.items.length === 0 ? <p className="py-5 text-sm text-muted-foreground">No Quick results yet.</p> : <ul className="divide-y divide-border">
      {history.items.map(run => <li key={run.id} className="py-4"><Link href={`/quick-research/${run.id}`} className="flex items-start gap-3 py-1 focus-visible:outline-2 focus-visible:outline-ring">
        <BookOpen className="mt-1 h-4 w-4 shrink-0 text-primary" /><div className="min-w-0 flex-1"><p className="text-sm font-medium leading-6 [overflow-wrap:anywhere]" dir="auto">{run.question}</p>
          <p className="mt-1 text-xs text-muted-foreground">{new Intl.DateTimeFormat("en-GB", { dateStyle: "medium", timeZone: "UTC" }).format(new Date(run.createdAt))}</p></div>
        <Badge tone={run.status === "Failed" ? "warning" : "neutral"}>{run.status}</Badge>
      </Link></li>)}
    </ul>}
    <div className="flex items-center justify-between border-t border-border pt-4 text-sm">
      <Button variant="ghost" size="sm" disabled={page === 1} onClick={() => setPage(value => value - 1)} aria-label="Previous page" title="Previous page"><ArrowLeft className="h-4 w-4" /></Button>
      <span>Page {page}</span>
      <Button variant="ghost" size="sm" disabled={page * history.pageSize >= history.totalCount} onClick={() => setPage(value => value + 1)} aria-label="Next page" title="Next page"><ArrowRight className="h-4 w-4" /></Button>
    </div>
  </div>;
}
