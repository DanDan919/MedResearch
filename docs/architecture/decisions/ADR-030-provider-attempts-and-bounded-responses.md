# ADR-030: Durable Provider Attempts and Bounded External Responses

Status: Accepted
Date: 2026-10-08

## Context

Successful LiteratureSearch rows alone cannot distinguish an unattempted provider from a failed one. ResponseHeadersRead does not apply HttpClient's headers timeout to later body reads, and result/page limits do not bound network payload bytes. Europe PMC indexing dates also previously entered publication metadata.

## Decision

Keep LiteratureSearch/Discovery as scientific success provenance. Persist a separate LiteratureProviderAttempt before each logical source/query execution with run/plan identity, bounded provider/query and start time. Finish once as SucceededWithResults, SucceededZeroResults, Failed, TimedOut or Cancelled with typed category/count/timestamps and optional successful search link. Started without a completion means no outcome was recorded; never infer the external call succeeded or failed. The ID identifies one logical execution, not each HTTP retry.

Successful completion and scientific search output share a short transaction. Failure completion has its own short transaction. Both, and attempt start, use the existing PostgreSQL owner/version/expiry fence. Status is an optimistic concurrency token as additional protection against double completion. Recovery reuses successful F9 execution keys; failed/unfinished executions can be reattempted under new IDs, retaining old facts. There is no exactly-once external request claim. Cancellation completion is bounded best effort (two seconds), fenced, then original cancellation is rethrown. Lease loss/storage failure cannot be swallowed as provider partial failure.

Use a small Infrastructure streaming reader with inclusive per-operation byte limits, caller cancellation and TimeProvider-backed body deadline. Never parse a partial success body. A body timeout may retry within the existing bounded transport policy; oversize and malformed payloads do not. Do not read non-success bodies or attach unsanitized network exceptions; disable HttpClientFactory URI logging. Default search body deadline: 15 seconds. Byte defaults: ESearch 256,000, EFetch/Europe PMC search 2,000,000; preserve full-text 2,000,000 and use its existing timeout as body deadline. Limits describe decoded stream bytes, not scientific completeness. Limiters remain process-local.

Europe PMC publication mapping ignores firstIndexDate. Prefer actual first publication, then print/electronic publication fields, then pubYear. Actual date parts win a conflicting explicit year. Missing precision is not invented; Study enrichment treats compatible date parts as a group and preserves conflicts. Do not mass-repair historical dates without authoritative provenance.

## Consequences

- Forward migration adds attempt metadata, run/plan/search foreign keys, run/start lookup index, unique non-null search link and outcome consistency check.
- The existing owner/run-scoped provenance endpoint and minimal frontend coverage list expose logical outcomes, not exception transcripts or a completeness score.
- Historical searches may have no attempt row. Absence is not proof of never-attempted historical coverage.
- Acquisition failure provenance has different Study/material identity and unavailable semantics; do not overload a literature query attempt for it. Its bounded durable model remains debt.
- Source trust, immutable evidence, statistical formulas, LLM repair and health policies are unchanged. No live provider is required for normal tests/CI.
- ADR-024's failed-search-history limitation is superseded here; its successful execution key and external-call race limitation remain applicable.
