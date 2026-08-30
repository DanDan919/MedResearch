# ADR-001: Start with a Layered Modular Monolith

Status: Accepted
Date: 2026-08-30

## Context

MedResearch is a portfolio and learning project that will eventually coordinate research planning, scientific metadata retrieval, evidence extraction, quality evaluation, conflict detection, and traceable synthesis. The future system may need background processing, persistence, external scientific APIs, LLM APIs, and possibly vector search.

The initial repository does not yet have production workflows, load characteristics, team boundaries, or operational needs that justify distributed infrastructure.

## Decision

Start with a layered modular monolith using separate .NET projects for API, Application, Domain, and Infrastructure. Keep Domain independent of external systems. Let Application orchestrate use cases. Let Infrastructure implement persistence and external adapters when those needs exist. Let API handle HTTP composition.

## Alternatives Considered

- Microservices: rejected because there are no demonstrated independent deployment, scaling, or ownership needs.
- Kafka/event-driven architecture: rejected because there is no current asynchronous workflow complexity requiring it.
- Kubernetes-based deployment: rejected because local Docker Compose is enough for the expected early development needs.
- Separate AI project: rejected for now because no concrete AI use case has been implemented yet.

## Consequences

The codebase starts easier to understand, test, and evolve. Architectural boundaries are enforced through project references rather than deployment boundaries. If later requirements justify separation, the layered design should make extraction easier without paying distributed-system costs on day one.
