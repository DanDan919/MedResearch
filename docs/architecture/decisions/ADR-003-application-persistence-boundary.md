# ADR-003: Use an Application Persistence Port for Research Creation and Retrieval

Status: Accepted
Date: 2026-08-30

## Context

MedResearch now needs its first end-to-end use case: accepting a research question through HTTP, creating a queued research run, and reading that run back. The repository already uses a layered monolith with EF Core and PostgreSQL in Infrastructure.

The API should not query EF Core directly, Domain should remain persistence-free, and no PubMed, background worker, queue, MediatR, or AI behavior is part of this milestone.

## Decision

Define a small Application-owned persistence boundary named `IResearchStore`. The boundary supports only the current use-case needs:

- Persist the initial `ResearchQuestion` and linked `ResearchRun` together.
- Retrieve a run projection for API readback.

Implement the boundary in Infrastructure through `EfResearchStore`. Keep EF Core details, transactions, and query composition inside Infrastructure. Return Application records from read operations rather than exposing EF entities to the API.

Register Application services through `services.AddApplication()` and Infrastructure services through `services.AddInfrastructure(configuration)` so `Program.cs` composes the layers without containing persistence implementation details.

## Alternatives Considered

- Querying `MedResearchDbContext` directly from API endpoints: rejected because it would bypass Application orchestration and make HTTP handlers own persistence concerns.
- A generic repository/unit-of-work abstraction: rejected for now because the current use case needs a narrow operation, not broad CRUD indirection.
- MediatR: rejected for this milestone because request dispatch would add ceremony before there are enough application workflows to justify it.
- Background queueing now: rejected because this milestone only records the queued run; execution comes later.

## Consequences

The API can create and retrieve research runs through Application use cases while keeping EF Core in Infrastructure. The initial question/run insert is transactional, so invalid relationships do not leave partial data. The port may evolve as source retrieval and background processing are added, but it should remain driven by use cases rather than storage mechanics.
