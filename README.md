# MedResearch

MedResearch is a portfolio and learning project for AI-assisted scientific evidence synthesis, focused primarily on medical and neuroscience research.

The purpose is not to diagnose patients or recommend treatments. The long-term goal is to help transform scientific questions into structured research workflows, retrieve study metadata, extract structured evidence, evaluate study quality where deterministic rules are possible, detect conflicting evidence, and produce traceable evidence syntheses.

## Current Scope

This repository currently contains the documentation system, layered .NET solution, PostgreSQL persistence through EF Core, a Docker Compose development environment, the first research API use case, and durable background processing for queued research runs.

A client can submit a research question, receive a queued research run id, and retrieve lifecycle progress. The background processor currently performs deterministic placeholder stages only. It does not yet implement research planning, scientific search, PubMed/Crossref integration, LLM extraction, RAG, or evidence synthesis workflows.

## Stack Direction

- C# and .NET 10
- ASP.NET Core Web API
- ASP.NET Core hosted background service
- EF Core and PostgreSQL
- Docker Compose for local development
- xUnit
- Testcontainers for PostgreSQL integration tests
- Structured logging through `ILogger`
- Separate worker executable later only if lifecycle or deployment needs justify it
- LLM APIs later
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
    MedResearch.IntegrationTests

docs/
    architecture/
        decisions/
    development/
```

## Local Development

Copy `.env.example` to `.env` if you want to override local ports or development database values. The checked-in values are development-only defaults and are not production secrets.

```bash
docker compose up --build
```

The API listens on `http://localhost:8080` by default. The health check is available at:

```text
GET /health
```

The Docker Compose API service sets `Database__ApplyMigrationsOnStartup=true`, so the committed EF migrations are applied when the local stack starts. The same API service hosts the background research worker. Background processing can be configured with `ResearchProcessing:Enabled` and `ResearchProcessing:IdleDelayMilliseconds`.

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

The background worker may move the run through `Planning`, `Searching`, `Extracting`, `Evaluating`, `Synthesizing`, and `Completed`. Invalid questions, missing runs, and server failures use ASP.NET Core Problem Details responses.

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

Application and API tests run without Docker by using Application persistence abstractions. PostgreSQL integration tests use Testcontainers and run against real PostgreSQL when Docker is reachable. They are skipped when Docker is installed but the engine is unavailable; they do not fall back to EF Core InMemory.

## Development Notes

Read `AGENTS.md`, `ARCHITECTURE.md`, and `docs/development/current-state.md` before significant changes.
