# Architecture

MedResearch is a modular, layered monolith. The system should remain simple until concrete needs justify more infrastructure.

## Dependency Direction

```text
Api
 v
Application
 v
Domain

Infrastructure
 v
Application / Domain
```

- `MedResearch.Domain` references no other MedResearch project.
- `MedResearch.Application` may reference Domain.
- `MedResearch.Infrastructure` may reference Application and Domain.
- `MedResearch.Api` composes Application and Infrastructure.

Domain must not reference EF Core, ASP.NET Core, HTTP clients, LLM SDKs, PostgreSQL libraries, or other external-system concerns.

## Project Responsibilities

- `MedResearch.Domain`: core research concepts, invariants, lifecycle rules, value decisions, and domain behavior.
- `MedResearch.Application`: use-case orchestration, application services, ports, transactions, and workflow coordination.
- `MedResearch.Infrastructure`: EF Core persistence, PostgreSQL configuration, background processing host adapter, external scientific APIs later, LLM clients later, and other adapters.
- `MedResearch.Api`: HTTP endpoints, request/response contracts, authentication, authorization, API composition, Problem Details responses, hosted worker process, and health check exposure.

Controllers and endpoints should not contain business rules.

## Application Use Cases

Implemented use cases are:

- `CreateResearchUseCase`: validates and records a research question, creates a linked queued `ResearchRun`, and logs the created run.
- `GetResearchUseCase`: retrieves a research run projection for API readback.
- `ResearchRunProcessor`: claims one queued run and advances it through deterministic placeholder pipeline stages using the existing domain lifecycle.

Application defines persistence ports that reflect current use cases:

- `IResearchStore`: create/read boundary for the HTTP research API.
- `IResearchRunQueue`: worker-specific boundary for claiming queued runs and persisting progress/failure state.

These are deliberately use-case-oriented rather than generic repositories. API endpoints call Application use cases and never query EF Core directly. Infrastructure implements the ports through EF Core/PostgreSQL adapters.

## HTTP API

The API currently exposes:

- `POST /api/research`: accepts a question and returns `201 Created` with the queued research run id. It does not wait for background processing.
- `GET /api/research/{researchRunId}`: returns the run state and original question, including `Failed` state and safe failure reason when present, or `404` when the run does not exist.
- `GET /health`: runs standard ASP.NET Core health checks, including the PostgreSQL DbContext check.

Validation and unexpected failures are returned as Problem Details. Internal exception details are logged but not exposed in server-error responses.

## Background Processing

The first background processor runs as an ASP.NET Core hosted `BackgroundService` in the API host. This keeps deployment simple for the current monolith while allowing multiple API process instances to compete safely for queued work through PostgreSQL.

The worker loop creates a scoped `ResearchRunProcessor`, attempts to claim one queued run, processes it if one is available, and waits for a configurable idle delay only when no queued work was found. The configured section is `ResearchProcessing` with `Enabled` and `IdleDelayMilliseconds` values.

The deterministic milestone pipeline advances runs through:

```text
Queued -> Planning -> Searching -> Extracting -> Evaluating -> Synthesizing -> Completed
```

No PubMed, external scientific APIs, generated studies, generated evidence, or AI calls are part of this worker milestone.

## PostgreSQL Claiming Strategy

`PostgreSqlResearchRunQueue` claims queued work with a short PostgreSQL transaction and an atomic `UPDATE ... WHERE id = (SELECT ... FOR UPDATE SKIP LOCKED ...) RETURNING ...` statement. The claim moves a run from `Queued` to `Planning` and sets `started_at` before committing.

Required invariant:

```text
One queued ResearchRun -> at most one worker can successfully claim it
```

The transaction boundary is intentionally short:

```text
begin transaction
claim one queued run and persist Planning
commit
execute deterministic stage work outside the transaction
persist each lifecycle transition separately
```

The worker does not hold a database lock for the full pipeline, which keeps the design compatible with future long-running external operations.

## Failure And Cancellation

If processing fails, Application logs the full exception internally and asks Infrastructure to persist the run as `Failed` with a safe failure reason. The API can then observe the failed state through `GET /api/research/{researchRunId}`.

Host shutdown cancellation is propagated as cancellation. It is not automatically recorded as a scientific processing failure.

If the process crashes after claiming a run and before completing or failing it, the run can remain in an in-progress state. Automatic lease/recovery is not implemented yet and is tracked as technical debt.

## Persistence

Persistence is implemented in `MedResearch.Infrastructure` with EF Core and Npgsql. `MedResearchDbContext` exposes sets for:

- `ResearchQuestion`
- `ResearchRun`
- `Study`
- `Evidence`

Entity mappings live in separate `IEntityTypeConfiguration<T>` classes under `src/MedResearch.Infrastructure/Persistence/Configurations`.

The first migration is `20260830063109_InitialCreate` under `src/MedResearch.Infrastructure/Persistence/Migrations`.

`EfResearchStore` persists the initial question/run pair in one EF Core transaction so a failed run insert cannot leave an orphaned question. Readback returns an Application projection rather than EF entities.

## Database Schema

- `research_questions`: GUID primary key, required question text, creation timestamp.
- `research_runs`: GUID primary key, required `research_question_id` FK, string-backed status, created/started/completed timestamps, optional failure reason.
- `studies`: GUID primary key, required title and source, optional abstract, DOI, PMID, journal, and publication date.
- `evidence`: GUID primary key, required `study_id` FK, required claim and direction, optional confidence.

Indexes are intentionally limited to current lookup/query needs:

- `research_runs(status, created_at)` for run status/queue-style queries.
- `studies(doi)` for DOI lookup when DOI is present.
- `studies(pmid)` for PMID lookup when PMID is present.
- EF-created FK indexes for relationships.

## Health Checks

The API maps standard ASP.NET Core health checks at `/health`. Infrastructure registers a DbContext health check named `postgresql`, so the endpoint proves the API can reach the configured PostgreSQL database.

## Local Docker Environment

Docker Compose defines:

- `postgres`: PostgreSQL 17 Alpine with development-only defaults.
- `api`: MedResearch API image built from `src/MedResearch.Api/Dockerfile`; it hosts both HTTP endpoints and the background research worker.

The compose API service enables config-gated startup migrations with `Database__ApplyMigrationsOnStartup=true`. This is a local development convenience, not a production migration strategy.

## Initial Domain Concepts

- `ResearchQuestion`: a scientific or medical research question with an id, trimmed question text, and creation timestamp. Empty or whitespace-only questions are rejected.
- `ResearchRun`: one execution of a future evidence pipeline for a question. Runs begin queued and move through explicit lifecycle methods.
- `ResearchRunStatus`: typed lifecycle states: queued, planning, searching, extracting, evaluating, synthesizing, completed, failed, and cancelled.
- `Study`: minimal normalized study metadata. External identifiers such as DOI and PMID may be missing.
- `Evidence`: minimal extracted claim linked to a study, with a direction and optional confidence.

## Research Run Lifecycle

A run begins as `Queued`. The valid happy path is:

```text
Queued -> Planning -> Searching -> Extracting -> Evaluating -> Synthesizing -> Completed
```

A non-terminal run may be failed with a required failure reason or cancelled. Completed, failed, and cancelled runs are terminal.

## Current Limitations

- No PubMed, Crossref, or other scientific source integration exists yet.
- No LLM integration exists yet.
- No RAG/vector search exists yet.
- No automatic recovery exists yet for runs left in progress after a process crash.
- Study quality evaluation and evidence synthesis are not modeled beyond minimal placeholders.
- Production migration strategy is not decided yet.
- OpenAPI document generation is intentionally not enabled until a non-vulnerable package set and concrete documentation need are chosen.
