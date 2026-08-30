# Current State

Date: 2026-08-30

## Exists Now

- Initial repository documentation and development trail.
- .NET 10 solution file: `MedResearch.slnx`.
- Local .NET tool manifest with `dotnet-ef`.
- Layered project skeleton:
  - `src/MedResearch.Api`
  - `src/MedResearch.Application`
  - `src/MedResearch.Domain`
  - `src/MedResearch.Infrastructure`
- Test projects:
  - `tests/MedResearch.Domain.Tests`
  - `tests/MedResearch.Application.Tests`
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
  - Placeholder stage execution is deterministic and does not generate scientific data.
  - Processing can be configured through `ResearchProcessing:Enabled` and `ResearchProcessing:IdleDelayMilliseconds`.
- Application persistence boundaries:
  - `IResearchStore` for HTTP create/read use cases.
  - `IResearchRunQueue` for worker claim/progress/failure operations.
  - `IResearchStageExecutor` for deterministic stage work now and replaceable stage execution later.
- EF Core PostgreSQL persistence in Infrastructure:
  - `MedResearchDbContext`
  - explicit entity configurations for ResearchQuestion, ResearchRun, Study, and Evidence
  - first migration: `20260830063109_InitialCreate`
  - `EfResearchStore` implements the HTTP persistence boundary
  - `PostgreSqlResearchRunQueue` implements atomic queued-work claiming using PostgreSQL `FOR UPDATE SKIP LOCKED`
- Docker Compose local development environment:
  - `postgres` service using PostgreSQL 17 Alpine
  - `api` service for `MedResearch.Api`, including the hosted background worker
  - development-only defaults in `.env.example`
  - `docker compose config` validates successfully
  - runtime startup is currently blocked by the local Docker Desktop engine being unreachable
- Minimal Domain concepts:
  - `ResearchQuestion`
  - `ResearchRun`
  - `ResearchRunStatus`
  - `Study`
  - `Evidence`
  - `EvidenceDirection`
- Domain unit tests for question validation and research run lifecycle behavior.
- Application unit tests for creating queued research runs, retrieving unknown runs, valid processing orchestration, failure transition, and shutdown cancellation behavior.
- API integration tests using `WebApplicationFactory` and a fake `IResearchStore`, so endpoint behavior runs without Docker and does not start hosted services.
- PostgreSQL integration tests using Testcontainers. They run against real PostgreSQL when Docker is reachable and are currently skipped because the Docker Desktop engine is unavailable. They do not fall back to EF Core InMemory.

## Environment Status

- GitHub remote is configured as `https://github.com/DanDan919/MedResearch.git`.
- Local `main` tracks `origin/main`; the previous application milestone was pushed successfully.
- GitHub CLI (`gh`) is not installed.
- Docker CLI and Docker Compose are installed, but Docker Desktop engine is not reachable at `//./pipe/dockerDesktopLinuxEngine`.
- Application/database health check runtime verification has not passed because the Docker Compose stack cannot start while the engine is unavailable.

## Next Logical Milestone

Add recovery for runs left in an in-progress state after process crashes, or add the first real scientific source client behind an Application-defined port. Do not add LLM extraction until source retrieval and traceability are designed.

## Not Yet Implemented

- Automatic recovery for in-progress runs after a worker crash.
- PubMed, Crossref, or other scientific source integrations.
- LLM integration.
- Evidence synthesis output.
- RAG/vector search.
- Production migration strategy.
