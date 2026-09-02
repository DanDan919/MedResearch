# MedResearch

MedResearch is a portfolio and learning project for AI-assisted scientific evidence synthesis, focused primarily on medical and neuroscience research.

The purpose is not to diagnose patients or recommend treatments. The long-term goal is to help transform scientific questions into structured research workflows, retrieve study metadata, extract structured evidence, evaluate study quality where deterministic rules are possible, detect conflicting evidence, and produce traceable evidence syntheses.

## Current Scope

This repository currently contains the documentation system, layered .NET solution, PostgreSQL persistence through EF Core, a Docker Compose development environment, the first research API use case, durable lease-backed background processing for queued and recoverable research runs, structured AI research planning through OpenAI, and scientific literature retrieval through PubMed/NCBI E-utilities, source-grounded abstract evidence extraction, structured source-aware evidence evaluation, and traceable persisted evidence synthesis reports.

A client can submit a research question, receive a queued research run id, and retrieve lifecycle progress. The background processor sends only the current submitted research question to the configured OpenAI provider during `Planning`, validates strict structured output into a persisted `ResearchPlan`, then uses accepted plan search queries during `Searching` to retrieve bounded PubMed metadata. During `Extracting`, it sends only the current question, bounded plan context, and one study title/abstract/metadata item to the configured OpenAI provider, validates strict structured output, and persists source-grounded abstract-level evidence. During `Evaluating`, it combines study metadata, extraction provenance, and grounded evidence into categorical methodological assessments. During `Synthesizing`, it builds a bounded current-run synthesis context and persists a traceable `ResearchReport`. It does not yet implement RAG, diagnosis, treatment recommendations, full-text synthesis, meta-analysis, formal GRADE, or formal risk-of-bias frameworks.

## Stack Direction

- C# and .NET 10
- ASP.NET Core Web API
- ASP.NET Core hosted background service
- EF Core and PostgreSQL
- OpenAI Responses API for strict structured planning, evidence extraction, evidence evaluation, and evidence synthesis output
- PubMed retrieval through official NCBI E-utilities
- Docker Compose for local development
- xUnit
- Testcontainers for PostgreSQL integration tests
- Structured logging through `ILogger`
- Separate worker executable later only if lifecycle or deployment needs justify it
- Additional LLM/scientific providers later behind provider-neutral abstractions
- pgvector later only if needed

## Repository Layout

```text
src/
    MedResearch.Api
    MedResearch.Application
    MedResearch.Domain
    MedResearch.Infrastructure

tests/
    MedResearch.Domain.Tests
    MedResearch.Application.Tests
    MedResearch.Infrastructure.Tests
    MedResearch.IntegrationTests

docs/
    architecture/
        decisions/
    development/
```

## Local Development

Copy `.env.example` to `.env` if you want to override local ports, development database values, OpenAI credentials, or PubMed options. The checked-in values are development-only defaults and placeholders, not production secrets.

```bash
docker compose up --build
```

The API listens on `http://localhost:8080` by default. The health check is available at:

```text
GET /health
GET /health/live
GET /health/ready
```

The Docker Compose API service sets `Database__ApplyMigrationsOnStartup=true`, so the committed EF migrations are applied when the local stack starts. The same API service hosts the background research worker.

Background processing can be configured with `ResearchProcessing:Enabled`, `ResearchProcessing:IdleDelayMilliseconds`, `ResearchProcessing:LeaseDurationSeconds`, and `ResearchProcessing:HeartbeatIntervalSeconds`. The heartbeat interval must be positive and shorter than the lease duration. Evidence extraction volume can be configured with `EvidenceExtraction:MaxStudiesPerRun`; the default is 10 and the application bounds it between 1 and 50. Evidence evaluation volume can be configured with `EvidenceEvaluation:MaxStudiesPerRun` with the same default and bounds. Synthesis context size can be configured with `Synthesis:MaxStudies`, `Synthesis:MaxEvidenceFindings`, and `Synthesis:MaxClaims`; defaults are 10, 40, and 12.

AI planning can be configured with:

- `AI:Provider`, currently only `OpenAI`
- `AI:BaseUrl`, default `https://api.openai.com/v1/`
- `AI:Model`, externally supplied
- `AI:ApiKey`, externally supplied secret
- `AI:TimeoutSeconds`, default 30
- `AI:MaxOutputTokens`, default 2000

PubMed can be configured with:

- `PubMed:BaseUrl`
- `PubMed:ResultLimit` development default: 10
- `PubMed:TimeoutSeconds` development default: 15
- `PubMed:Tool`
- `PubMed:Email`
- `PubMed:ApiKey`
- `PubMed:RequestIntervalMilliseconds` development default: 350

Use `.env`, user secrets, or CI secrets for real OpenAI and NCBI API keys. Do not commit `.env`. The API can start and expose health endpoints without an OpenAI API key; a real processing run that reaches an OpenAI-backed stage fails through the normal safe failure path if required provider configuration is absent.

## Research API

Create a queued research run from a question:

```text
POST /api/research
Content-Type: application/json

{
  "question": "Does chronic sleep deprivation impair working memory in adults?"
}
```

Successful responses return `201 Created`, a `Location` header, and the queued run id:

```json
{
  "researchRunId": "00000000-0000-0000-0000-000000000000",
  "status": "Queued"
}
```

Retrieve the current run state:

```text
GET /api/research/{researchRunId}
```

The lease-backed background worker may move the run through `Planning`, `Searching`, `Extracting`, `Evaluating`, `Synthesizing`, and `Completed`. If a worker disappears mid-run, another worker can reclaim an expired in-progress lease and retry from the persisted current stage. Invalid questions, missing runs, not-ready reports, and server failures use ASP.NET Core Problem Details responses.

Retrieve the persisted synthesis report:

```text
GET /api/research/{researchRunId}/report
```

The report endpoint returns `200 OK` with coverage, deterministic limitations, claims, and authoritative Evidence/Study citations when a report exists. It returns `404 Not Found` for an unknown run and `409 Conflict` for a known run whose report is not ready.

## Research Planning

`Planning` uses a provider-neutral Application boundary for strict structured generation. Infrastructure currently implements that boundary with the OpenAI Responses API using JSON Schema structured output.

The planner output is treated as untrusted external input. It is deserialized, validated by Application, and only then persisted as `ResearchPlan`. The authoritative `ResearchQuestion` remains separate; the LLM-generated `originalQuestion` field must match the stored question after whitespace normalization and cannot overwrite it.

The prompt version is `research-planner-v1`. The planner is allowed to produce question decomposition and search strategy only. It must not produce PMIDs, DOIs, invented papers, authors, effect sizes, sample sizes, confidence intervals, p-values, evidence grades, diagnoses, treatments, or scientific conclusions.

## Scientific Retrieval

`Searching` currently uses PubMed only. The flow is:

```text
ResearchQuestion -> ResearchPlan -> SearchQueries -> ESearch PMIDs -> EFetch XML -> normalized Study candidates -> PostgreSQL
```

Multiple planned queries are executed sequentially. Each query creates its own `LiteratureSearch` provenance row linked to the originating `ResearchPlan`. If more than one query discovers the same Study, MedResearch preserves multiple discovery paths while downstream extraction and synthesis process the Study once per ResearchRun. Zero PubMed results for a valid query are persisted as a zero-result search and are not treated as infrastructure failure.

Stored study metadata is limited to values reported by PubMed: PMID, DOI, title, abstract, journal, publication date/date parts, publication types, authors, and source. Missing values stay null or empty; the system does not infer scientific facts.

## Evidence Extraction

`Extracting` currently works at abstract level only. A study with no usable PubMed abstract is recorded as a skipped extraction with `NoExtractableText`; it is not sent to the LLM provider and does not fail the run.

Extracted `Evidence` rows are tied to both the global `Study` and the specific `ResearchRun`. Each completed attempt also creates an `EvidenceExtraction` provenance row with provider, model, prompt version, source scope, extraction timestamp, evidence count, and grounding validation status.

The prompt version is `evidence-extractor-v1`. Supporting excerpts must be present in the supplied abstract after deterministic normalization. Numeric fields are persisted only when the value appears in the supplied source text; otherwise they remain null.

## Evidence Evaluation

`Evaluating` creates one study-level `EvidenceEvaluation` per research run, study, and evaluator prompt version. It stores the grounded `EvidenceIds` considered, structured methodological domains, deterministic signal booleans, source-scope limitations, provenance, and a bounded overall methodological confidence category.

Evaluation uses categorical states rather than arbitrary numeric quality scores. `Unknown` means MedResearch cannot determine a value from available validated information. `InsufficientSource` means the current source scope is not adequate for the judgment. `NotApplicable` means the domain does not conceptually apply. Source absence must not become a negative quality judgment.

The prompt version is `evidence-evaluator-v1`. The evaluator reuses `IStructuredLlmClient`; OpenAI remains an Infrastructure adapter. Normal tests use fake LLM providers. This is not GRADE, Cochrane RoB 2, ROBINS-I, AMSTAR-2, Newcastle-Ottawa Scale, or another validated framework.

## Evidence Synthesis

`Synthesizing` creates a persisted `ResearchReport` for the current research run. It uses only validated current-run Evidence, current-run Study metadata, search provenance, extraction provenance, and study-level EvidenceEvaluation records.

The prompt version is `research-synthesizer-v1`. The synthesis model returns strict structured output, but Application still validates every draft claim. Persisted claims must cite supplied EvidenceIds, cannot provide their own PMID/DOI/study identifiers, and must preserve direction semantics. Citation metadata returned by the API is reconstructed from persisted Evidence and Study rows.

When no validated evidence exists, MedResearch creates an explicit `InsufficientEvidence` report without calling the LLM. Synthesis is qualitative only: no meta-analysis, vote counting, formal GRADE, formal risk-of-bias result, diagnosis, or treatment recommendation is produced.

## CI

GitHub Actions runs on Ubuntu with Docker available. The workflow restores, builds, runs the full test suite with Testcontainers required, fails if Docker-required CI reports skipped tests, checks for pending EF model changes, validates Docker Compose, and uploads TRX test results for diagnostics. The Testcontainers fixture applies EF migrations to a fresh PostgreSQL database before PostgreSQL integration tests execute.

## EF Core Migrations

Restore local tools before running EF commands on a fresh machine:

```bash
dotnet tool restore
```

Create a migration:

```bash
dotnet ef migrations add MigrationName --project src/MedResearch.Infrastructure/MedResearch.Infrastructure.csproj --startup-project src/MedResearch.Api/MedResearch.Api.csproj --output-dir Persistence/Migrations
```

Apply migrations to a configured database:

```bash
dotnet ef database update --project src/MedResearch.Infrastructure/MedResearch.Infrastructure.csproj --startup-project src/MedResearch.Api/MedResearch.Api.csproj
```

## Validation

```bash
dotnet restore
dotnet build
dotnet test
docker compose config
```

Domain, Application, Infrastructure, architecture-boundary, and API tests run without Docker. Planner, evidence extractor, evidence evaluator, and evidence synthesizer tests use fake LLM providers. OpenAI adapter tests use fake HTTP and do not call the live OpenAI API. PubMed adapter tests use local fixtures and fake HTTP; they do not call the live internet. PostgreSQL integration tests use Testcontainers and run against real PostgreSQL when Docker is reachable. They are skipped locally when Docker is installed but the engine is unavailable; they do not fall back to EF Core InMemory. CI sets `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true`, so Docker/Testcontainers unavailability fails the run instead of silently skipping PostgreSQL coverage. Normal CI does not require `OPENAI_API_KEY`, NCBI credentials, or live internet calls to OpenAI/PubMed.

## Development Notes

Read `AGENTS.md`, `ARCHITECTURE.md`, and `docs/development/current-state.md` before significant changes.
