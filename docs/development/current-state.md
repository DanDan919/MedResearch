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
- Test project skeleton:
  - `tests/MedResearch.Domain.Tests`
  - `tests/MedResearch.Application.Tests`
  - `tests/MedResearch.IntegrationTests`
- Standard ASP.NET Core health check endpoint at `/health`.
- Infrastructure registration through `services.AddInfrastructure(configuration)`.
- EF Core PostgreSQL persistence in Infrastructure:
  - `MedResearchDbContext`
  - explicit entity configurations for ResearchQuestion, ResearchRun, Study, and Evidence
  - first migration: `InitialCreate`
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
- PostgreSQL integration tests using Testcontainers. They run against real PostgreSQL when Docker is reachable and are currently skipped because the Docker Desktop engine is unavailable.

## Environment Status

- GitHub remote is configured as `https://github.com/DanDan919/MedResearch.git`.
- Push is not operational yet: GitHub returns `Repository not found` for the configured remote.
- GitHub CLI (`gh`) is not installed, so this environment cannot create `DanDan919/MedResearch` through `gh` or inspect `gh` authentication.
- Docker CLI and Docker Compose are installed, but Docker Desktop engine is not reachable at `//./pipe/dockerDesktopLinuxEngine`.
- Application/database health check runtime verification has not passed because the Docker Compose stack cannot start while the engine is unavailable.

## Next Logical Milestone

Introduce the first real application use case: submitting a research question and creating a queued research run through the API. That milestone should add application orchestration, request/response contracts, and persistence through Infrastructure without adding scientific API clients or LLM extraction yet.

## Not Yet Implemented

- A user-facing research question submission endpoint.
- Repository/application service abstractions for saving research questions and runs.
- Structured logging.
- Background workers.
- PubMed, Crossref, or other scientific source integrations.
- LLM integration.
- Evidence synthesis output.
- RAG/vector search.
- Production migration strategy.

