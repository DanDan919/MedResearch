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
- Minimal Domain concepts:
  - `ResearchQuestion`
  - `ResearchRun`
  - `ResearchRunStatus`
  - `Study`
  - `Evidence`
  - `EvidenceDirection`
- Domain unit tests for question validation and research run lifecycle behavior.
- PostgreSQL integration tests using Testcontainers. They run against real PostgreSQL when Docker is reachable and are skipped when the Docker engine is unavailable.

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
