# Technical Debt

## Current

- Production migration strategy is not decided. Docker Compose uses config-gated startup migrations for local development only.
- Claimed research runs do not have a lease or automatic recovery path yet. If a process crashes after moving a run from `Queued` to an in-progress status, the run can remain in that state until an operator or future recovery workflow handles it.
- PubMed retrieval has no bounded retry policy yet. Network failures, timeouts, rate limiting, invalid upstream responses, and parsing failures currently move the run through the existing safe failure path.
- PubMed request pacing is conservative but local to one process. There is no distributed rate limiter across multiple API instances.
- The deterministic PubMed query builder is intentionally simple and not scientifically optimized.

## Watch List

- Design a PostgreSQL-backed recovery strategy for stale in-progress `ResearchRun` rows before adding real long-running LLM calls.
- Decide whether the hosted background worker should become a separate `MedResearch.Worker` executable once independent deployment, scaling, or operational lifecycle needs are demonstrated.
- Replace deterministic question-to-query conversion with a Research Planner that produces structured search plans.
- Add additional literature source adapters only when they actually work and are covered by fixtures/tests.
- Decide whether `/health` should become a richer operational health check when more external dependencies exist.
- Run PostgreSQL Testcontainers integration tests in an environment where Docker Desktop is running.
