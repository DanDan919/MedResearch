"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { ArrowRight, ChevronLeft, ChevronRight, Plus } from "lucide-react";
import { researchPipelineStages, researchRunStatusPresentation } from "@medresearch/api";
import type { ResearchRunStatus } from "@medresearch/api";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Skeleton } from "@medresearch/ui";
import { useResearchRuns } from "../../lib/api";
import { EmptyState, ErrorPanel } from "../state-panel";
import { ResearchStatusPill } from "./research-status-pill";

const pageSize = 20;
const statusOptions: readonly ResearchRunStatus[] = [
  ...researchPipelineStages,
  "Failed",
  "Cancelled"
];

export function ResearchHistory() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const page = readPositiveInt(searchParams.get("page")) ?? 1;
  const status = readStatus(searchParams.get("status"));
  const query = useResearchRuns({ page, pageSize, status });

  function setQuery(next: { page?: number; status?: ResearchRunStatus | null }) {
    const parameters = new URLSearchParams(searchParams.toString());
    const nextPage = next.page ?? page;

    if (nextPage > 1) {
      parameters.set("page", String(nextPage));
    } else {
      parameters.delete("page");
    }

    if ("status" in next) {
      if (next.status) {
        parameters.set("status", next.status);
      } else {
        parameters.delete("status");
      }
    }

    const queryString = parameters.toString();
    router.push(queryString ? `${pathname}?${queryString}` : pathname);
  }

  const data = query.data;
  const showEmpty = data && data.totalCount === 0 && !status;
  const showFilteredEmpty = data && data.totalCount === 0 && status;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-normal">Research</h1>
          <p className="mt-2 max-w-3xl text-sm text-muted-foreground">
            Review submitted research runs, open an existing run, and continue from the backend lifecycle state.
          </p>
        </div>
        <Button asChild>
          <Link href="/research/new">
            <Plus className="h-4 w-4" />
            New Research
          </Link>
        </Button>
      </div>

      <Card>
        <CardContent className="flex flex-col gap-3 py-4 sm:flex-row sm:items-end">
          <label className="grid gap-1 text-sm">
            <span className="font-medium">Status</span>
            <select
              className="h-9 rounded-md border border-border bg-surface px-3 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring"
              value={status ?? ""}
              onChange={(event) => setQuery({ page: 1, status: readStatus(event.target.value) })}
            >
              <option value="">All statuses</option>
              {statusOptions.map((option) => (
                <option key={option} value={option}>
                  {researchRunStatusPresentation[option].label}
                </option>
              ))}
            </select>
          </label>
        </CardContent>
      </Card>

      {query.isLoading ? <HistorySkeleton /> : null}

      {query.isError ? (
        <ErrorPanel title="Research history unavailable" message="The API could not return the research history page." />
      ) : null}

      {showEmpty ? (
        <div className="space-y-4">
          <EmptyState
            title="No research yet"
            description="Start your first research run to build an evidence-backed report."
          />
          <Button asChild>
            <Link href="/research/new">Start research</Link>
          </Button>
        </div>
      ) : null}

      {showFilteredEmpty ? (
        <EmptyState
          title="No research matches this status"
          description="Change the status filter or start a new research run."
        />
      ) : null}

      {data && data.items.length > 0 ? (
        <div className="space-y-3">
          {data.items.map((item) => (
            <Card key={item.researchRunId}>
              <CardHeader className="gap-3 sm:flex sm:flex-row sm:items-start sm:justify-between">
                <div>
                  <CardTitle className="text-base">{item.question}</CardTitle>
                  <CardDescription>
                    Created {formatTimestamp(item.createdAt)}
                    {item.completedAt ? ` · Completed ${formatTimestamp(item.completedAt)}` : ""}
                  </CardDescription>
                </div>
                <ResearchStatusPill status={item.status} />
              </CardHeader>
              <CardContent className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <div className="text-sm text-muted-foreground">
                  {item.failureReason ? <span className="text-destructive">{item.failureReason}</span> : "Run summary"}
                </div>
                <Button asChild variant="secondary">
                  <Link href={`/research/${item.researchRunId}`}>
                    Open <ArrowRight className="h-4 w-4" />
                  </Link>
                </Button>
              </CardContent>
            </Card>
          ))}

          <div className="flex items-center justify-between gap-3">
            <Button
              type="button"
              variant="secondary"
              disabled={data.page <= 1}
              onClick={() => setQuery({ page: Math.max(1, data.page - 1) })}
            >
              <ChevronLeft className="h-4 w-4" />
              Previous
            </Button>
            <div className="text-sm text-muted-foreground">
              Page {data.page} of {Math.max(data.totalPages, 1)}
            </div>
            <Button
              type="button"
              variant="secondary"
              disabled={data.totalPages === 0 || data.page >= data.totalPages}
              onClick={() => setQuery({ page: data.page + 1 })}
            >
              Next
              <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
        </div>
      ) : null}
    </div>
  );
}

function HistorySkeleton() {
  return (
    <div className="space-y-3">
      {[0, 1, 2].map((item) => (
        <Card key={item}>
          <CardContent className="space-y-3 py-4">
            <Skeleton className="h-4 w-3/4" />
            <Skeleton className="h-4 w-1/3" />
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

function readPositiveInt(value: string | null): number | undefined {
  if (!value) {
    return undefined;
  }

  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : undefined;
}

function readStatus(value: string | null): ResearchRunStatus | undefined {
  if (!value) {
    return undefined;
  }

  return statusOptions.includes(value as ResearchRunStatus) ? (value as ResearchRunStatus) : undefined;
}

function formatTimestamp(value: string): string {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(new Date(value));
}
