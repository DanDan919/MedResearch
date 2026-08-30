# Architecture

MedResearch starts as a modular, layered monolith. The system should remain simple until concrete needs justify more infrastructure.

## Dependency Direction

```text
Api
 v
Application
 v
Domain

Infrastructure
 v
Application / Domain
```

- `MedResearch.Domain` references no other MedResearch project.
- `MedResearch.Application` may reference Domain.
- `MedResearch.Infrastructure` may reference Application and Domain.
- `MedResearch.Api` composes Application and Infrastructure.

Domain must not reference EF Core, ASP.NET Core, HTTP clients, LLM SDKs, PostgreSQL libraries, or other external-system concerns.

## Project Responsibilities

- `MedResearch.Domain`: core research concepts, invariants, lifecycle rules, value decisions, and domain behavior.
- `MedResearch.Application`: use-case orchestration, application services, ports, transactions, and workflow coordination.
- `MedResearch.Infrastructure`: persistence, external scientific APIs, background processing implementation, LLM clients, and other adapters.
- `MedResearch.Api`: HTTP endpoints, request/response contracts, authentication, authorization, and API composition.

Controllers and endpoints should not contain business rules.

## Initial Domain Concepts

- `ResearchQuestion`: a scientific or medical research question with an id, trimmed question text, and creation timestamp. Empty or whitespace-only questions are rejected.
- `ResearchRun`: one execution of a future evidence pipeline for a question. Runs begin queued and move through explicit lifecycle methods.
- `ResearchRunStatus`: typed lifecycle states: queued, planning, searching, extracting, evaluating, synthesizing, completed, failed, and cancelled.
- `Study`: minimal normalized study metadata. External identifiers such as DOI and PMID may be missing.
- `Evidence`: minimal extracted claim linked to a study, with a direction and optional confidence.

## Research Run Lifecycle

A run begins as `Queued`. The valid happy path is:

```text
Queued -> Planning -> Searching -> Extracting -> Evaluating -> Synthesizing -> Completed
```

A non-terminal run may be failed with a required failure reason or cancelled. Completed, failed, and cancelled runs are terminal.

## Current Limitations

- No database or EF Core model has been added yet.
- No PubMed, Crossref, or other scientific source integration exists yet.
- No background worker exists yet.
- No LLM integration exists yet.
- No RAG/vector search exists yet.
- Study quality evaluation and evidence synthesis are not modeled beyond minimal placeholders.
