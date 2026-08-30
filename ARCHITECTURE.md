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

Domain must not reference EF Core, ASP.NET Core, HTTP clients, LLM SDKs, PostgreSQL libraries, NCBI transport models, or other external-system concerns.

## Project Responsibilities

- `MedResearch.Domain`: core research concepts, invariants, lifecycle rules, scientific metadata identity, and domain behavior.
- `MedResearch.Application`: use-case orchestration, application services, provider-neutral ports, transactions, and workflow coordination.
- `MedResearch.Infrastructure`: EF Core persistence, PostgreSQL configuration, hosted worker adapter, PubMed/NCBI integration, external scientific APIs later, LLM clients later, and other adapters.
- `MedResearch.Api`: HTTP endpoints, request/response contracts, authentication, authorization, API composition, Problem Details responses, hosted worker process, and health check exposure.

Controllers and endpoints should not contain business rules.

## Application Use Cases

Implemented use cases are:

- `CreateResearchUseCase`: validates and records a research question, creates a linked queued `ResearchRun`, and logs the created run.
- `GetResearchUseCase`: retrieves a research run projection for API readback.
- `ResearchRunProcessor`: claims one queued run and advances it through the existing domain lifecycle.
- `ScientificResearchStageExecutor`: performs real scientific retrieval during `Searching` and deterministic no-op behavior for stages that are not implemented yet.

Application defines ports that reflect current use cases:

- `IResearchStore`: create/read boundary for the HTTP research API.
- `IResearchRunQueue`: worker-specific boundary for claiming queued runs and persisting progress/failure state.
- `IScientificLiteratureSource`: provider-neutral scientific search boundary.
- `IScientificSearchResultStore`: persistence boundary for normalized scientific candidates, search provenance, and discovery links.
- `IScientificSearchQueryBuilder`: deterministic query creation for now; a future Research Planner can replace this without changing source adapters.

These are deliberately use-case-oriented rather than generic repositories. API endpoints call Application use cases and never query EF Core directly. Infrastructure implements the ports through EF Core/PostgreSQL and PubMed adapters.

## HTTP API

The API currently exposes:

- `POST /api/research`: accepts a question and returns `201 Created` with the queued research run id. It does not wait for background processing.
- `GET /api/research/{researchRunId}`: returns the run state and original question, including `Failed` state and safe failure reason when present, or `404` when the run does not exist.
- `GET /health`: runs standard ASP.NET Core health checks, including the PostgreSQL DbContext check.

Validation and unexpected failures are returned as Problem Details. Internal exception details are logged but not exposed in server-error responses.

## Background Processing

The first background processor runs as an ASP.NET Core hosted `BackgroundService` in the API host. This keeps deployment simple for the current monolith while allowing multiple API process instances to compete safely for queued work through PostgreSQL.

The worker loop creates a scoped `ResearchRunProcessor`, attempts to claim one queued run, processes it if one is available, and waits for a configurable idle delay only when no queued work was found. The configured section is `ResearchProcessing` with `Enabled` and `IdleDelayMilliseconds` values.

The current pipeline advances runs through:

```text
Queued -> Planning -> Searching -> Extracting -> Evaluating -> Synthesizing -> Completed
```

`Searching` retrieves real PubMed metadata. Planning, Extracting, Evaluating, and Synthesizing remain deterministic placeholders and do not generate fake evidence.

## Scientific Retrieval

Application depends on provider-neutral scientific retrieval contracts. Infrastructure currently provides one implementation: PubMed through official NCBI E-utilities.

Current PubMed flow:

```text
ResearchQuestion
  -> deterministic bounded query
  -> ESearch db=pubmed returns PMIDs
  -> EFetch db=pubmed retmode=xml returns article metadata
  -> PubMed transport parsing in Infrastructure
  -> ScientificStudyCandidate records
  -> PostgreSQL persistence
```

The deterministic query builder trims and bounds the original research question. It is not scientifically optimal; a later AI Research Planner may produce structured search plans and multiple optimized queries. Scientific source adapters and future LLM adapters must remain separate concerns.

PubMed configuration lives under `PubMed`:

- `BaseUrl`
- `ResultLimit`, default 10 and bounded in code
- `TimeoutSeconds`, default 15
- `Tool`, default `MedResearch`
- optional `Email`
- optional `ApiKey`
- `RequestIntervalMilliseconds`, default 350

The adapter uses HttpClientFactory and does not create a new HttpClient per request. It performs ESearch and EFetch sequentially and does not aggressively parallelize PubMed calls. No live PubMed tests run by default.

## Scientific Data Integrity

External source data is untrusted input. PubMed transport models remain in Infrastructure and are normalized before crossing into Application-facing contracts.

The system stores values reported by PubMed when available:

- PMID
- DOI
- title
- abstract
- journal
- publication date
- publication year/month/day parts for incomplete dates
- publication types
- authors
- source

Missing values stay missing. The system does not infer missing DOI, PMID, authors, publication types, sample sizes, effect sizes, confidence intervals, or conclusions.

## Study Identity And Search Provenance

`Study` represents global scientific study identity. Stable identifiers drive deduplication:

- PMID is preferred for PubMed records.
- DOI is used where appropriate.
- Fuzzy title matching is not used.

`LiteratureSearch` records which source was searched, the query sent, when it ran, how many results were returned, how many studies were newly persisted, and how many were duplicates.

`ResearchStudyDiscovery` links a `ResearchRun`, a `LiteratureSearch`, and a global `Study`. This preserves the distinction between a paper existing globally and a paper being discovered during one research run.

## PostgreSQL Claiming Strategy

`PostgreSqlResearchRunQueue` claims queued work with a short PostgreSQL transaction and an atomic `UPDATE ... WHERE id = (SELECT ... FOR UPDATE SKIP LOCKED ...) RETURNING ...` statement. The claim moves a run from `Queued` to `Planning`, sets `started_at`, and returns the associated research question text before committing.

Required invariant:

```text
One queued ResearchRun -> at most one worker can successfully claim it
```

The transaction boundary is intentionally short:

```text
begin transaction
claim one queued run and persist Planning
commit
execute deterministic/external stage work outside the transaction
persist each lifecycle transition separately
```

The worker does not hold a database lock for the full pipeline, which keeps the design compatible with external operations.

## Failure And Cancellation

If processing fails, Application logs the full exception internally and asks Infrastructure to persist the run as `Failed` with a safe failure reason. The API can then observe the failed state through `GET /api/research/{researchRunId}`.

PubMed network failures, timeouts, rate limiting, invalid upstream responses, and parsing failures are converted into source exceptions and follow the same safe run failure path. Host shutdown cancellation is propagated as cancellation and is not automatically recorded as a scientific processing failure.

If the process crashes after claiming a run and before completing or failing it, the run can remain in an in-progress state. Automatic lease/recovery is not implemented yet and is tracked as technical debt.

## Persistence

Persistence is implemented in `MedResearch.Infrastructure` with EF Core and Npgsql. `MedResearchDbContext` exposes sets for:

- `ResearchQuestion`
- `ResearchRun`
- `Study`
- `Evidence`
- `LiteratureSearch`
- `ResearchStudyDiscovery`

Entity mappings live in separate `IEntityTypeConfiguration<T>` classes under `src/MedResearch.Infrastructure/Persistence/Configurations`.

Migrations:

- `20260830063109_InitialCreate`
- `20260830114130_AddLiteratureSearchProvenance`

## Database Schema

- `research_questions`: GUID primary key, required question text, creation timestamp.
- `research_runs`: GUID primary key, required `research_question_id` FK, string-backed status, created/started/completed timestamps, optional failure reason.
- `studies`: GUID primary key, required title and source, optional abstract, DOI, PMID, journal, publication date/date parts, publication types, and authors.
- `literature_searches`: GUID primary key, required `research_run_id` FK, source, query, searched timestamp, result count, persisted count, duplicate count.
- `research_study_discoveries`: GUID primary key, required FKs to research run, literature search, and study, plus source identifier and discovery timestamp.
- `evidence`: GUID primary key, required `study_id` FK, required claim and direction, optional confidence.

Indexes and constraints are intentionally limited to current lookup/query needs:

- `research_runs(status, created_at)` for run status/queue-style queries.
- filtered unique `studies(doi)` for DOI deduplication when DOI is present.
- filtered unique `studies(pmid)` for PMID deduplication when PMID is present.
- `literature_searches(research_run_id, searched_at)` for run provenance lookup.
- unique `research_study_discoveries(research_run_id, study_id)` so one run does not link the same global study twice.
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
- `Study`: normalized scientific metadata reported by external sources. Missing source data remains missing.
- `LiteratureSearch`: minimal reproducibility record for a source query run during a research run.
- `ResearchStudyDiscovery`: association between one research run/search execution and a global study.
- `Evidence`: minimal extracted claim linked to a study, with a direction and optional confidence. Evidence is not generated yet.

## Current Limitations

- No Crossref, Europe PMC, OpenAlex, Semantic Scholar, or publisher source integration exists yet.
- No AI Research Planner exists yet; PubMed uses a deterministic query from the original question.
- No LLM integration exists yet.
- No RAG/vector search exists yet.
- No automatic recovery exists yet for runs left in progress after a process crash.
- Study quality evaluation and evidence synthesis are not modeled beyond minimal placeholders.
- PubMed retry policy is not implemented; failures are surfaced to the existing run failure path.
- Rate limiting is conservative and local to the process; no distributed rate limiter exists.
- Production migration strategy is not decided yet.
- OpenAPI document generation is intentionally not enabled until a non-vulnerable package set and concrete documentation need are chosen.
