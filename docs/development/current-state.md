# Current State

Date: 2026-08-30

## Exists Now

- Initial repository documentation and development trail.
- .NET 10 solution file: `MedResearch.slnx`.
- Layered project skeleton:
  - `src/MedResearch.Api`
  - `src/MedResearch.Application`
  - `src/MedResearch.Domain`
  - `src/MedResearch.Infrastructure`
- Test project skeleton:
  - `tests/MedResearch.Domain.Tests`
  - `tests/MedResearch.Application.Tests`
  - `tests/MedResearch.IntegrationTests`
- Minimal health endpoint at `/health`.
- Minimal Domain concepts:
  - `ResearchQuestion`
  - `ResearchRun`
  - `ResearchRunStatus`
  - `Study`
  - `Evidence`
  - `EvidenceDirection`
- Domain unit tests for question validation and research run lifecycle behavior.

## Next Logical Milestone

Introduce the first real application use case, likely submitting a research question and creating a queued research run. That milestone should add application orchestration and API request/response contracts without adding persistence or external scientific API clients until needed.

## Not Yet Implemented

- EF Core and PostgreSQL persistence.
- Docker Compose.
- Structured logging.
- Background workers.
- PubMed, Crossref, or other scientific source integrations.
- LLM integration.
- Evidence synthesis output.
- RAG/vector search.
