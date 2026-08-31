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

Domain must not reference EF Core, ASP.NET Core, HTTP clients, LLM SDKs, PostgreSQL libraries, NCBI transport models, OpenAI transport models, API keys, or other external-system concerns.

## Project Responsibilities

- `MedResearch.Domain`: core research concepts, invariants, lifecycle rules, accepted research plans, scientific metadata identity, and domain behavior.
- `MedResearch.Application`: use-case orchestration, application services, provider-neutral ports, validation of external structured output, transactions, and workflow coordination.
- `MedResearch.Infrastructure`: EF Core persistence, PostgreSQL configuration, hosted worker adapter, OpenAI structured-generation adapter, PubMed/NCBI integration, external scientific APIs later, and other adapters.
- `MedResearch.Api`: HTTP endpoints, request/response contracts, authentication, authorization, API composition, Problem Details responses, hosted worker process, and health check exposure.

Controllers and endpoints should not contain business rules.

## Application Use Cases

Implemented use cases are:

- `CreateResearchUseCase`: validates and records a research question, creates a linked queued `ResearchRun`, and logs the created run.
- `GetResearchUseCase`: retrieves a research run projection for API readback.
- `ResearchRunProcessor`: claims one queued run and advances it through the existing domain lifecycle.
- `ResearchPlanner`: sends the current question through a provider-neutral structured LLM boundary, validates the result, and persists an accepted `ResearchPlan`.
- `ScientificResearchStageExecutor`: performs structured planning during `Planning`, PubMed retrieval during `Searching`, source-grounded abstract evidence extraction during `Extracting`, and deterministic no-op behavior for stages that are not implemented yet.

Application defines ports that reflect current use cases:

- `IResearchStore`: create/read boundary for the HTTP research API.
- `IResearchRunQueue`: worker-specific boundary for claiming queued runs and persisting progress/failure state.
- `IStructuredLlmClient`: provider-neutral boundary for strict structured generation.
- `IResearchPlanStore`: persistence boundary for accepted research plans.
- `IScientificLiteratureSource`: provider-neutral scientific search boundary.
- `IScientificSearchResultStore`: persistence boundary for normalized scientific candidates, search provenance, and discovery links.
- `IEvidenceExtractor`: provider-neutral Application service for validating one study extraction request.
- `IEvidenceExtractionStore`: persistence boundary for discovered-study extraction work items, extraction provenance, and extracted findings.

These are deliberately use-case-oriented rather than generic repositories. API endpoints call Application use cases and never query EF Core directly. Infrastructure implements the ports through EF Core/PostgreSQL, OpenAI, and PubMed adapters.

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

`Planning` produces a validated persisted `ResearchPlan`. `Searching` retrieves real PubMed metadata from accepted plan search queries. `Extracting` now performs abstract-level structured evidence extraction from discovered studies. Evaluating and Synthesizing remain deterministic placeholders and do not generate fake evidence.

## AI Research Planning

Application depends on provider-neutral structured-generation contracts. Infrastructure currently provides one implementation: OpenAI through the Responses API.

Current planning flow:

```text
ResearchQuestion
  -> ResearchPlanner
  -> IStructuredLlmClient
  -> OpenAI Responses API strict JSON Schema output
  -> ResearchPlanDraft
  -> Application validation
  -> ResearchPlan persistence
```

The LLM is an untrusted planning component, not a scientific authority. It is allowed to decompose the question and propose search strategy only. It must not produce PMIDs, DOIs, invented paper titles, invented authors, effect sizes, sample sizes, confidence intervals, p-values, evidence grades, diagnoses, treatments, or scientific conclusions.

The external LLM input is narrowly scoped to the current submitted research question and planner instructions. Retrieved article abstracts, evidence, unrelated research runs, database records, API keys, authentication headers, and raw provider payloads are not sent as part of this milestone.

The prompt version is `research-planner-v1`. The prompt lives in `ResearchPlannerPrompt` rather than being buried inside the service method. It defines role, allowed task, prohibited behavior, uncertainty behavior, allowed study-type labels, and search-query constraints.

Validation remains mandatory even with provider-side schema enforcement. Application validates at least:

- the LLM-provided `originalQuestion` matches the authoritative stored question after whitespace normalization;
- search queries are present;
- search query count is at most five;
- search query length is at most 300 characters;
- blank search queries are rejected;
- duplicate queries are removed case-insensitively;
- list fields are bounded;
- optional text fields are bounded;
- preferred study types must use supported labels;
- query text must not contain obvious stable study identifiers such as PMID or DOI.

OpenAI configuration lives under `AI`:

- `Provider`, currently only `OpenAI`
- `BaseUrl`, default `https://api.openai.com/v1/`
- `Model`, externally supplied
- `ApiKey`, externally supplied secret
- `TimeoutSeconds`, default 30
- `MaxOutputTokens`, default 2000 and bounded in code

If OpenAI is selected but model or API key configuration is missing, the provider call fails clearly and the existing run failure path records a safe external failure reason. The app does not silently substitute fake AI behavior.

## Scientific Retrieval

Application depends on provider-neutral scientific retrieval contracts. Infrastructure currently provides one implementation: PubMed through official NCBI E-utilities.

Current PubMed flow:

```text
ResearchPlan.SearchQueries
  -> sequential source search requests
  -> ESearch db=pubmed returns PMIDs
  -> EFetch db=pubmed retmode=xml returns article metadata
  -> PubMed transport parsing in Infrastructure
  -> ScientificStudyCandidate records
  -> PostgreSQL persistence
```

The previous deterministic question-to-query builder has been removed. Searching does not derive PubMed queries directly from the raw research question; it consumes the accepted `ResearchPlan.SearchQueries` produced during Planning.

PubMed configuration lives under `PubMed`:

- `BaseUrl`
- `ResultLimit`, default 10 and bounded in code
- `TimeoutSeconds`, default 15
- `Tool`, default `MedResearch`
- optional `Email`
- optional `ApiKey`
- `RequestIntervalMilliseconds`, default 350

The adapter uses HttpClientFactory and does not create a new HttpClient per request. It performs ESearch and EFetch sequentially and does not aggressively parallelize PubMed calls. Multiple planned queries are also executed sequentially. No live PubMed tests run by default.

A valid plan may produce zero PubMed results. Zero-result searches are persisted and the pipeline may continue to later placeholder stages without fabricating studies or evidence.

## Evidence Extraction

Application performs evidence extraction through `EvidenceExtractor`, which reuses the existing provider-neutral `IStructuredLlmClient`. Infrastructure continues to own the concrete OpenAI HTTP adapter; Application does not depend on OpenAI-specific contracts.

Current extraction flow:

```text
ResearchStudyDiscovery + Study abstract
  -> EvidenceExtractor
  -> IStructuredLlmClient
  -> strict JSON Schema output
  -> deterministic grounding and numeric validation
  -> EvidenceExtraction provenance + Evidence findings
```

The approved LLM input scope is limited to the current research question, bounded `ResearchPlan` context, and the selected `Study` title, abstract, and metadata. No unrelated persisted records, secrets, raw provider payloads, or full-text claims are sent.

The prompt version is `evidence-extractor-v1`. The prompt requires abstract-level extraction only, source-only behavior, null for absent fields, bounded direction labels, and no prose. The LLM is an extraction tool, not a scientific authority.

Validation is deterministic:

- `supportingText` must be a short excerpt present in the supplied abstract after whitespace/case normalization.
- blank, excessive, or fabricated supporting excerpts fail validation.
- numeric fields are kept only when the numeric value appears in the supplied abstract; otherwise they remain null.
- duplicate findings are deduplicated before persistence.
- unsupported directions or study design labels are rejected.

A missing abstract is recorded as a skipped extraction with `NoExtractableText` and does not call the LLM. Provider, structured-output, validation, and grounding failures use the existing safe run failure path. Extraction runs sequentially and is bounded by `EvidenceExtraction:MaxStudiesPerRun`, default 10.

## Scientific Data Integrity

External source data is untrusted input. OpenAI and PubMed transport models remain in Infrastructure and are normalized or deserialized before crossing into Application-facing contracts.

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

`ResearchPlan` records accepted structured planning output for one research run, including provider, model, prompt version, and generated timestamp.

`LiteratureSearch` records which source was searched, which query was sent, when it ran, how many results were returned, how many studies were newly persisted, how many were duplicates, and the optional `ResearchPlanId` that produced the query.

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
execute external stage work outside the transaction
persist each lifecycle transition separately
```

The worker does not hold a database lock for the full pipeline, which keeps the design compatible with external LLM and scientific API operations.

## Failure And Cancellation

If processing fails, Application logs the full exception internally and asks Infrastructure to persist the run as `Failed` with a safe failure reason. The API can then observe the failed state through `GET /api/research/{researchRunId}`.

OpenAI configuration failures, authentication failures, timeouts, rate limiting, network failures, malformed structured responses, and validation failures follow the existing safe run failure path. PubMed network failures, timeouts, rate limiting, invalid upstream responses, and parsing failures follow the same path. Host shutdown cancellation is propagated as cancellation and is not automatically recorded as a scientific processing failure.

If the process crashes after claiming a run and before completing or failing it, the run can remain in an in-progress state. Automatic lease/recovery is not implemented yet and is tracked as technical debt.

## Persistence

Persistence is implemented in `MedResearch.Infrastructure` with EF Core and Npgsql. `MedResearchDbContext` exposes sets for:

- `ResearchQuestion`
- `ResearchRun`
- `ResearchPlan`
- `Study`
- `EvidenceExtraction`
- `Evidence`
- `LiteratureSearch`
- `ResearchStudyDiscovery`

Entity mappings live in separate `IEntityTypeConfiguration<T>` classes under `src/MedResearch.Infrastructure/Persistence/Configurations`.

Migrations:

- `20260830063109_InitialCreate`
- `20260830114130_AddLiteratureSearchProvenance`
- `20260830160612_AddStructuredResearchPlans`

## Database Schema

- `research_questions`: GUID primary key, required question text, creation timestamp.
- `research_runs`: GUID primary key, required `research_question_id` FK, string-backed status, created/started/completed timestamps, optional failure reason.
- `research_plans`: GUID primary key, required `research_run_id` and `research_question_id` FKs, authoritative original question, optional PICO-like text fields, array fields for outcomes, preferred study types, search queries, and exclusion hints, plus provider, model, prompt version, and generated timestamp.
- `studies`: GUID primary key, required title and source, optional abstract, DOI, PMID, journal, publication date/date parts, publication types, and authors.
- `literature_searches`: GUID primary key, required `research_run_id` FK, optional `research_plan_id` FK, source, query, searched timestamp, result count, persisted count, duplicate count.
- `research_study_discoveries`: GUID primary key, required FKs to research run, literature search, and study, plus source identifier and discovery timestamp.
- `evidence`: GUID primary key, required `study_id` FK, required claim and direction, optional confidence.

Indexes and constraints are intentionally limited to current lookup/query needs:

- `research_runs(status, created_at)` for run status/queue-style queries.
- unique `research_plans(research_run_id)` because one accepted plan belongs to one run.
- `research_plans(research_question_id)` for question-to-plan lookup.
- filtered unique `studies(doi)` for DOI deduplication when DOI is present.
- filtered unique `studies(pmid)` for PMID deduplication when PMID is present.
- `literature_searches(research_run_id, searched_at)` for run provenance lookup.
- `literature_searches(research_plan_id)` for plan provenance lookup.
- unique `research_study_discoveries(research_run_id, study_id)` so one run does not link the same global study twice.
- unique `evidence_extractions(research_run_id, study_id, prompt_version)` for extraction idempotency.
- `evidence_extractions(research_run_id, status)` for run extraction status queries.
- `evidence(research_run_id)`, `evidence(study_id)`, and `evidence(evidence_extraction_id)` for later run and source traceability lookups.
- EF-created FK indexes for relationships.

## Health Checks

The API maps standard ASP.NET Core health checks at `/health`. Infrastructure registers a DbContext health check named `postgresql`, so the endpoint proves the API can reach the configured PostgreSQL database.

## Local Docker Environment

Docker Compose defines:

- `postgres`: PostgreSQL 17 Alpine with development-only defaults.
- `api`: MedResearch API image built from `src/MedResearch.Api/Dockerfile`; it hosts both HTTP endpoints and the background research worker.

The compose API service enables config-gated startup migrations with `Database__ApplyMigrationsOnStartup=true`. This is a local development convenience, not a production migration strategy. OpenAI model and API key values are supplied through environment variables or `.env`; committed configuration contains placeholders only.

## Initial Domain Concepts

- `ResearchQuestion`: a scientific or medical research question with an id, trimmed question text, and creation timestamp. Empty or whitespace-only questions are rejected.
- `ResearchRun`: one execution of a future evidence pipeline for a question. Runs begin queued and move through explicit lifecycle methods.
- `ResearchRunStatus`: typed lifecycle states: queued, planning, searching, extracting, evaluating, synthesizing, completed, failed, and cancelled.
- `ResearchPlan`: validated structured planning output for decomposition and search strategy. It is not evidence or synthesis.
- `Study`: normalized scientific metadata reported by external sources. Missing source data remains missing.
- `LiteratureSearch`: minimal reproducibility record for a source query run during a research run.
- `ResearchStudyDiscovery`: association between one research run/search execution and a global study.
- `EvidenceExtraction`
- `EvidenceExtraction`: one extraction attempt/status/provenance row for one research run, one study, and one prompt version.
- `Evidence`: one source-grounded abstract-level finding linked to a research run, study, and extraction attempt. Supporting excerpts must be traceable to the supplied study abstract.

## Current Limitations

- No Crossref, Europe PMC, OpenAlex, Semantic Scholar, or publisher source integration exists yet.
- OpenAI is the only implemented LLM provider.
- No live OpenAI smoke test is configured or run by default.
- No full-text extraction, evidence synthesis output, or RAG/vector search exists yet.
- No automatic recovery exists yet for runs left in progress after a process crash.
- Study quality evaluation and evidence synthesis are not modeled beyond minimal placeholders.
- PubMed retry policy is not implemented; failures are surfaced to the existing run failure path.
- OpenAI retry policy is not implemented; failures are surfaced to the existing run failure path.
- Rate limiting is conservative and local to the process; no distributed rate limiter exists.
- Production migration strategy is not decided yet.
- OpenAPI document generation is intentionally not enabled until a non-vulnerable package set and concrete documentation need are chosen.
