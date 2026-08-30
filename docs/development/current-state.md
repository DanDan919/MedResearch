# Current State

Date: 2026-08-30

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
  - `Searching` now performs real PubMed retrieval through provider-neutral Application contracts.
  - Planning, Extracting, Evaluating, and Synthesizing remain deterministic placeholders and do not generate fake evidence.
  - Processing can be configured through `ResearchProcessing:Enabled` and `ResearchProcessing:IdleDelayMilliseconds`.
- Scientific literature retrieval:
  - `IScientificLiteratureSource`
  - `IScientificSearchQueryBuilder`
  - `IScientificSearchResultStore`
  - PubMed implementation using official NCBI E-utilities `esearch.fcgi` and `efetch.fcgi`
  - deterministic question-to-query builder with bounded query length
  - PubMed result limit default: 10
  - PubMed timeout default: 15 seconds
  - optional PubMed/NCBI email and API key configuration
- Application persistence boundaries:
  - `IResearchStore` for HTTP create/read use cases.
  - `IResearchRunQueue` for worker claim/progress/failure operations.
  - `IScientificSearchResultStore` for normalized scientific candidates, search provenance, and discovery links.
- EF Core PostgreSQL persistence in Infrastructure:
  - `MedResearchDbContext`
  - explicit entity configurations for ResearchQuestion, ResearchRun, Study, Evidence, LiteratureSearch, and ResearchStudyDiscovery
  - migrations:
    - `20260830063109_InitialCreate`
    - `20260830114130_AddLiteratureSearchProvenance`
  - `EfResearchStore` implements the HTTP persistence boundary
  - `PostgreSqlResearchRunQueue` implements atomic queued-work claiming using PostgreSQL `FOR UPDATE SKIP LOCKED`
  - `EfScientificSearchResultStore` persists PubMed study metadata, search provenance, and discovery links
- Docker Compose local development environment:
  - `postgres` service using PostgreSQL 17 Alpine
  - `api` service for `MedResearch.Api`, including the hosted background worker
  - development-only defaults in `.env.example`
  - PubMed environment placeholders in `.env.example`
  - `docker compose config` validates successfully
  - runtime startup is currently blocked by the local Docker Desktop engine being unreachable
- Domain concepts:
  - `ResearchQuestion`
  - `ResearchRun`
  - `ResearchRunStatus`
  - `Study`
  - `LiteratureSearch`
  - `ResearchStudyDiscovery`
  - `Evidence`
  - `EvidenceDirection`
- Tests:
  - Domain unit tests for question validation and research run lifecycle behavior.
  - Application tests for queued run creation, retrieval miss, processing orchestration, search stage invocation, empty search results, source failure, and cancellation behavior.
  - Infrastructure tests for PubMed ESearch parsing, EFetch XML mapping, missing metadata, incomplete publication dates, malformed upstream XML, fake-HTTP request flow, and rate-limit error conversion.
  - API integration tests using `WebApplicationFactory` and a fake `IResearchStore`, so endpoint behavior runs without Docker and does not start hosted services.
  - PostgreSQL integration tests using Testcontainers. They run against real PostgreSQL when Docker is reachable and are currently skipped because the Docker Desktop engine is unavailable. They do not fall back to EF Core InMemory.

## Environment Status

- GitHub remote is configured as `https://github.com/DanDan919/MedResearch.git`.
- Local `main` tracks `origin/main`; the previous background-processing milestone was pushed successfully.
- GitHub CLI (`gh`) is not installed.
- Docker CLI and Docker Compose are installed, but Docker Desktop engine is not reachable at `//./pipe/dockerDesktopLinuxEngine`.
- Application/database health check runtime verification has not passed because the Docker Compose stack cannot start while the engine is unavailable.
- No live PubMed smoke test is configured or run by default. Normal tests use fixtures and fake HTTP.

## Next Logical Milestone

Add recovery for runs left in an in-progress state after process crashes, or improve scientific search planning before adding additional literature sources. Do not add LLM extraction until retrieval traceability and planner boundaries are designed.

## Not Yet Implemented

- Automatic recovery for in-progress runs after a worker crash.
- Crossref, Europe PMC, OpenAlex, Semantic Scholar, publisher API, or other non-PubMed source integrations.
- AI Research Planner.
- LLM integration.
- Evidence synthesis output.
- RAG/vector search.
- PubMed retry policy or distributed rate limiting.
- Production migration strategy.
