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
- First end-to-end research use case:
  - `POST /api/research` creates a `ResearchQuestion` and queued `ResearchRun`.
  - `GET /api/research/{researchRunId}` retrieves the run state and original question.
  - API endpoints call Application use cases and do not query EF directly.
  - Invalid input and missing runs are returned as Problem Details.
- Application persistence boundary:
  - `IResearchStore`
  - `CreateResearchUseCase`
  - `GetResearchUseCase`
- EF Core PostgreSQL persistence in Infrastructure:
  - `MedResearchDbContext`
  - explicit entity configurations for ResearchQuestion, ResearchRun, Study, and Evidence
  - first migration: `20260830063109_InitialCreate`
  - `EfResearchStore` implements the Application persistence boundary
- Docker Compose local development environment:
  - `postgres` service using PostgreSQL 17 Alpine
  - `api` service for `MedResearch.Api`
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
- Application unit tests for creating queued research runs and retrieving unknown runs without Docker.
- API integration tests using `WebApplicationFactory` and a fake `IResearchStore`, so endpoint behavior runs without Docker.
- PostgreSQL integration tests using Testcontainers. They run against real PostgreSQL when Docker is reachable and are currently skipped because the Docker Desktop engine is unavailable. They do not fall back to EF Core InMemory.

## Environment Status

- GitHub remote is configured as `https://github.com/DanDan919/MedResearch.git`.
- Push is still not operational from this environment. Earlier attempts returned `Repository not found`; the latest attempt reached a network failure connecting to `github.com:443`.
- GitHub CLI (`gh`) is not installed, so this environment cannot create `DanDan919/MedResearch` through `gh` or inspect `gh` authentication.
- Docker CLI and Docker Compose are installed, but Docker Desktop engine is not reachable at `//./pipe/dockerDesktopLinuxEngine`.
- Application/database health check runtime verification has not passed because the Docker Compose stack cannot start while the engine is unavailable.

## Next Logical Milestone

Add the first background execution step for queued research runs, or add a scientific source client such as PubMed behind an Application-defined port. Do not add LLM extraction until source retrieval and traceability are designed.

## Not Yet Implemented

- Background workers or queues.
- PubMed, Crossref, or other scientific source integrations.
- LLM integration.
- Evidence synthesis output.
- RAG/vector search.
- Production migration strategy.
