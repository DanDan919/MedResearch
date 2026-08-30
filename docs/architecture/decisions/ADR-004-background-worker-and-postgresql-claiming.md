# ADR-004: Host Background Research Processing in the API with PostgreSQL Atomic Claiming

Status: Accepted
Date: 2026-08-30

## Context

Research creation now records a queued `ResearchRun`, but execution must move out of the HTTP request lifecycle. `POST /api/research` should return `201 Created` immediately while a background processor advances queued runs through the existing domain lifecycle.

The system is still a layered monolith. There is no PubMed integration, AI integration, external scientific API, queue broker, or need for a separately deployed worker process yet. At the same time, more than one API process may run in the future, so claiming queued work must be safe across processes and cannot rely on in-memory locks.

## Decision

Run the first background processor as an ASP.NET Core hosted `BackgroundService` inside `MedResearch.Api`. Keep the processing orchestration in Application and the database-backed claim implementation in Infrastructure.

Introduce an Application-owned `IResearchRunQueue` port for worker-specific persistence operations:

- claim the next queued run,
- persist lifecycle progress,
- persist safe failure state.

Implement the port in Infrastructure with PostgreSQL using a short atomic claim statement:

```sql
UPDATE research_runs
SET status = @planning_status,
    started_at = @claimed_at
WHERE id = (
    SELECT id
    FROM research_runs
    WHERE status = @queued_status
    ORDER BY created_at, id
    FOR UPDATE SKIP LOCKED
    LIMIT 1
)
RETURNING id, research_question_id, status, created_at, started_at, completed_at, failure_reason;
```

The claim runs in a short `ReadCommitted` transaction and commits before deterministic pipeline stages execute. The worker does not hold a database transaction or row lock for the whole pipeline. Each later state transition is persisted separately through the queue port.

For this milestone, the stage executor performs deterministic no-op stage work. It does not generate fake studies or evidence.

## Alternatives Considered

- Separate `MedResearch.Worker` executable: deferred because the current deployment model is one API plus PostgreSQL, and a separate process would add operational surface without a demonstrated lifecycle or scaling need.
- In-memory locks such as `lock`, `SemaphoreSlim`, or static collections: rejected because they do not protect against multiple processes.
- Holding a transaction open for the full pipeline: rejected because future scientific API calls and LLM calls would make long-held row locks unsafe.
- A generic repository abstraction: rejected because the worker needs a specific queue/claim boundary, not broad CRUD operations.
- A full lease/recovery subsystem now: deferred because it is meaningful but larger than the first durable worker milestone.

## Consequences

`POST /api/research` remains fast, while run progress becomes visible through `GET /api/research/{researchRunId}`. Multiple worker instances can compete for queued runs without actively processing the same queued run at the same time.

If a process crashes after claiming a run, the run can remain in an in-progress state because no lease/recovery mechanism exists yet. This limitation is documented as technical debt.
