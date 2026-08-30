# Technical Debt

## Current

- Production migration strategy is not decided. Docker Compose uses config-gated startup migrations for local development only.
- Claimed research runs do not have a lease or automatic recovery path yet. If a process crashes after moving a run from `Queued` to an in-progress status, the run can remain in that state until an operator or future recovery workflow handles it.

## Watch List

- Design a PostgreSQL-backed recovery strategy for stale in-progress `ResearchRun` rows before adding real long-running external source retrieval or LLM calls.
- Decide whether the hosted background worker should become a separate `MedResearch.Worker` executable once independent deployment, scaling, or operational lifecycle needs are demonstrated.
- Decide whether `/health` should become a richer operational health check when more external dependencies exist.
- Run PostgreSQL Testcontainers integration tests in an environment where Docker Desktop is running.
