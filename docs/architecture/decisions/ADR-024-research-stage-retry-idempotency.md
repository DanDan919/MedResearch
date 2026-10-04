# ADR-024: Idempotent Recovery for Planning and Successful Searching

- Status: Accepted
- Date: 2026-10-04

## Context

`ResearchRun` recovery resumes the persisted current stage after a processing lease expires. A process can crash after a stage writes durable output but before it advances the run status. Without an idempotent stage boundary, recovery can either insert a duplicate plan or repeat a successful external literature search and create misleading duplicate provenance.

## Decision

Planning has one canonical persisted `ResearchPlan` per `ResearchRun`. On re-entry, a matching question/prompt contract reuses the existing plan. A conflicting question, research-question identity, or planner prompt version is rejected. The database unique constraint remains the final authority for concurrent plan inserts; an equivalent unique-race result returns the canonical row.

Successful literature search provenance has the execution key:

```text
(ResearchRunId, ResearchPlanId, Source, Query)
```

The coordinator checks for an existing successful execution before calling a provider. PostgreSQL also enforces a unique index for the key, and the persistence store resolves an equivalent unique race to the existing `LiteratureSearch` row. Distinct planned queries and sources remain distinct provenance paths. Study identity and discovery uniqueness are unchanged.

## Consequences

- Sequential Planning and Searching recovery is idempotent for already successful output.
- A matching plan is not regenerated through the LLM on normal stage replay.
- A successful search is not called again on sequential stage replay.
- Provider calls can still race before a successful execution row exists; first-class in-progress provider attempt reservation is deferred.
- Failed provider attempts remain operational logs rather than persisted failed `LiteratureSearch` status rows. Zero-result successful searches remain persisted and distinct from exceptions.
- No database transaction is held across planner or provider I/O.

## Verification

Application tests cover plan reuse and search coordinator reuse without a second fake provider call. PostgreSQL integration tests cover equivalent plan retry and equivalent search execution retry. The forward migration `AddLiteratureSearchExecutionIdempotency` adds the unique search execution index. Local Docker-backed tests are skipped when Docker is unavailable; CI must execute them with strict Docker-test enforcement.
