# Current State

Date: 2026-08-31

## Exists Now

- Initial repository documentation and development trail.
- .NET 10 solution file: `MedResearch.slnx`.
- Local .NET tool manifest with `dotnet-ef`.
- Layered projects:
  - `src/MedResearch.Api`
  - `src/MedResearch.Application`
  - `src/MedResearch.Domain`
  - `src/MedResearch.Infrastructure`
- Test projects:
  - `tests/MedResearch.Domain.Tests`
  - `tests/MedResearch.Application.Tests`
  - `tests/MedResearch.Infrastructure.Tests`
  - `tests/MedResearch.IntegrationTests`
- Standard ASP.NET Core health check endpoint at `/health`.
- Infrastructure registration through `services.AddInfrastructure(configuration)`.
- Application registration through `services.AddApplication()`.
- First end-to-end research API use case:
  - `POST /api/research` creates a `ResearchQuestion` and queued `ResearchRun`.
  - `GET /api/research/{researchRunId}` retrieves the run state and original question.
  - API endpoints call Application use cases and do not query EF directly.
  - Invalid input, missing runs, and unexpected failures are returned as Problem Details.
- Durable background research processing:
  - `BackgroundResearchWorker` runs as an ASP.NET Core hosted service in `MedResearch.Api`.
  - `ResearchRunProcessor` advances claimed runs through Planning, Searching, Extracting, Evaluating, Synthesizing, and Completed.
  - `Planning` calls the structured Research Planner and persists a validated `ResearchPlan`.
  - `Searching` consumes persisted `ResearchPlan.SearchQueries` and performs real PubMed retrieval through provider-neutral Application contracts.
  - `Extracting` performs source-grounded abstract-level evidence extraction for discovered studies.
  - Evaluating and Synthesizing remain deterministic placeholders and do not generate fake conclusions.
- Structured AI research planning:
  - `IStructuredLlmClient` provider-neutral Application boundary.
  - `ResearchPlanner` Application service.
  - `ResearchPlannerPrompt` with prompt version `research-planner-v1`.
  - OpenAI Infrastructure adapter using the Responses API with strict JSON Schema structured output.
  - Normal tests use fake LLM providers or fake HTTP and do not call the live OpenAI API.
- Scientific literature retrieval:
  - `IScientificLiteratureSource`
  - `IScientificSearchResultStore`
  - PubMed implementation using official NCBI E-utilities `esearch.fcgi` and `efetch.fcgi`
  - PubMed result limit default: 10
  - optional PubMed/NCBI email and API key configuration
  - multiple planned queries execute sequentially
  - zero-result searches are persisted and do not fabricate studies or evidence
- Source-grounded evidence extraction:
  - `IEvidenceExtractor` and `EvidenceExtractor` in Application.
  - `IEvidenceExtractionStore` implemented by `EfEvidenceExtractionStore` in Infrastructure.
  - Prompt version `evidence-extractor-v1` with strict structured output.
  - LLM input scope is limited to the current question, bounded plan context, and one study title/abstract/metadata item.
  - Studies with no usable abstract are recorded as skipped with `NoExtractableText` and are not sent to the LLM.
  - Supporting excerpts are validated deterministically against the supplied abstract.
  - Numeric fields are persisted only when the same numeric value appears in supplied source text; otherwise they remain null.
  - Provider, malformed output, validation, and grounding failures use the existing safe run failure path.
  - `EvidenceExtraction:MaxStudiesPerRun` defaults to 10 and is bounded between 1 and 50.
- Application persistence boundaries:
  - `IResearchStore` for HTTP create/read use cases.
  - `IResearchRunQueue` for worker claim/progress/failure operations.
  - `IResearchPlanStore` for accepted ResearchPlan persistence and lookup.
  - `IScientificSearchResultStore` for normalized scientific candidates, search provenance, and discovery links.
  - `IEvidenceExtractionStore` for extraction work items, provenance, idempotency, and evidence persistence.
- EF Core PostgreSQL persistence in Infrastructure:
  - `MedResearchDbContext`
  - explicit entity configurations for ResearchQuestion, ResearchRun, ResearchPlan, Study, Evidence, EvidenceExtraction, LiteratureSearch, and ResearchStudyDiscovery
  - migrations:
    - `20260830063109_InitialCreate`
    - `20260830114130_AddLiteratureSearchProvenance`
    - `20260830160612_AddStructuredResearchPlans`
    - `20260831142411_AddSourceGroundedEvidenceExtraction`
- Docker Compose local development environment:
  - `postgres` service using PostgreSQL 17 Alpine
  - `api` service for `MedResearch.Api`, including the hosted background worker
  - development-only defaults in `.env.example`
  - OpenAI and PubMed environment placeholders in `.env.example`
  - `EvidenceExtraction__MaxStudiesPerRun` wired from `.env.example`
- Domain concepts:
  - `ResearchQuestion`
  - `ResearchRun`
  - `ResearchRunStatus`
  - `ResearchPlan`
  - `Study`
  - `LiteratureSearch`
  - `ResearchStudyDiscovery`
  - `EvidenceExtraction`
  - `EvidenceExtractionStatus`
  - `EvidenceExtractionSkipReason`
  - `Evidence`
  - `EvidenceDirection`
  - `EvidenceSourceScope`
- Tests:
  - Domain unit tests for question validation and research run lifecycle behavior.
  - Application tests for queued run creation, retrieval miss, processing orchestration, planner validation, original-question preservation, planning failure, search behavior, evidence extraction validation, grounding, numeric grounding, skips, deduplication, cancellation, and provider failure propagation.
  - Infrastructure tests for OpenAI Responses API request/response mapping and PubMed parsing/fake HTTP behavior.
  - API integration tests using `WebApplicationFactory` and a fake `IResearchStore`, so endpoint behavior runs without Docker and does not start hosted services.
  - PostgreSQL integration tests using Testcontainers for research persistence, queue semantics, plan/search persistence, and evidence extraction persistence. They run against real PostgreSQL when Docker is reachable and are currently skipped because the Docker Desktop engine is unavailable. They do not fall back to EF Core InMemory.

## Environment Status

- GitHub remote is configured as `https://github.com/DanDan919/MedResearch.git`.
- Docker CLI and Docker Compose are installed, but Docker Desktop engine is not reachable at `//./pipe/dockerDesktopLinuxEngine` in this environment.
- Application/database health check runtime verification has not passed because the Docker Compose stack cannot start while the engine is unavailable.
- No live PubMed smoke test is configured or run by default. Normal tests use fixtures and fake HTTP.
- No live OpenAI smoke test is configured or run by default. Normal tests use fake LLM providers and fake HTTP.

## Next Logical Milestone

Add deterministic study quality evaluation using the persisted `Evidence` and `EvidenceExtraction` provenance. Do not add synthesis or medical recommendations before evaluation and traceability are reviewed.

## Not Yet Implemented

- Automatic recovery for in-progress runs after a worker crash.
- Crossref, Europe PMC, OpenAlex, Semantic Scholar, publisher API, or other non-PubMed source integrations.
- Additional LLM providers beyond OpenAI.
- Full-text extraction.
- Study quality evaluation.
- Evidence synthesis output.
- RAG/vector search.
- PubMed retry policy or distributed rate limiting.
- OpenAI retry policy.
- Production migration strategy.
