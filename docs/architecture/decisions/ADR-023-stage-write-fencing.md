# ADR-023: fencing stage writes after ResearchRun lease transfer

## Status

Accepted

## Context

`PostgreSqlResearchRunQueue` already fenced lifecycle progress with worker id and monotonic lease version. Stage stores, however, originally accepted only a ResearchRun id. A slow worker could therefore wake after lease expiry/reclaim and persist a plan, search result, Evidence, quantitative artifact, or report after another worker had taken ownership.

## Decision

The worker scope attaches its `ClaimedResearchRun` to `IResearchRunWriteFence`. Production EF stage stores call the fence inside their short write transaction. The PostgreSQL implementation selects the claimed `research_runs` row `FOR UPDATE` and requires matching owner, lease version, active status, and non-expired lease. Source material writes additionally require that the Study was discovered by the claimed run.

The transaction is never held across LLM or scientific-provider I/O. A failed fence throws `ResearchRunLeaseLostException`; the old worker does not mark the run failed or continue writing.

## Consequences

- A transferred lease fences stage output, not only lifecycle progress.
- Existing direct store tests can still construct stores without a worker fence; production DI always registers the PostgreSQL fence.
- Direct SQL outside the application write protocol is not covered.
- No schema migration is required because the existing lease owner/version columns are the fencing token.
