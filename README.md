# MedResearch

MedResearch is a portfolio and learning project for AI-assisted scientific evidence synthesis, focused primarily on medical and neuroscience research.

The purpose is not to diagnose patients or recommend treatments. The long-term goal is to help transform scientific questions into structured research workflows, retrieve study metadata, extract structured evidence, evaluate study quality where deterministic rules are possible, detect conflicting evidence, and produce traceable evidence syntheses.

## Current Scope

This repository currently contains the initial documentation system and a layered .NET solution skeleton. It does not yet implement research planning, scientific search, persistence, LLM extraction, RAG, or synthesis workflows.

## Stack Direction

- C# and .NET 10
- ASP.NET Core Web API
- xUnit
- EF Core and PostgreSQL later
- Docker Compose later
- Structured logging later
- Background workers later
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

## Validation

```bash
dotnet restore
dotnet build
dotnet test
```

## Development Notes

Read `AGENTS.md`, `ARCHITECTURE.md`, and `docs/development/current-state.md` before significant changes.
