# ADR-006: Add Structured AI Research Planning Through a Provider-Neutral LLM Boundary

Status: Accepted
Date: 2026-08-30

## Context

The background pipeline can retrieve PubMed metadata during `Searching`, but search queries are currently produced by deterministic trimming of the raw research question. The next milestone needs the first real AI component: a structured Research Planner that decomposes the submitted question and produces bounded scientific search queries.

The LLM is not a scientific authority. It is an untrusted external planning component. The system must not allow generated prose, invented evidence, or provider-specific DTOs to enter trusted application state without validation.

This milestone is authorized to send only the current submitted research question and planner instructions to the configured OpenAI API. It is not authorized to send unrelated persisted data, retrieved abstracts, evidence, API keys, authentication headers, unrelated research runs, or raw database records.

## Decision

Add a small Application-owned structured-generation boundary:

- `IStructuredLlmClient` accepts a prompt, prompt version, and structured output schema.
- The result returns a typed value plus non-secret provider metadata: provider, model, optional response id, and generated timestamp.
- Application contracts do not expose OpenAI SDK types, HTTP DTOs, model-specific classes, API keys, or provider response objects.

Implement one real provider in Infrastructure: OpenAI through the Responses API with JSON Schema structured output and `strict: true`. The adapter uses `HttpClientFactory`, configurable base URL, model, API key, timeout, and bounded output tokens. Normal tests use fake HTTP and do not call the live OpenAI API.

Add `ResearchPlanner` in Application. It sends the current question through `IStructuredLlmClient`, deserializes the strict structured result into `ResearchPlanDraft`, validates it, and persists an accepted `ResearchPlan` through `IResearchPlanStore`.

Use prompt version `research-planner-v1`. Keep the prompt in a maintainable prompt component instead of embedding an ad hoc string inside a service method.

Persist `ResearchPlan` as relational scalar columns plus PostgreSQL text arrays for bounded list fields. This keeps ownership, provenance, and common lookup fields queryable without creating many low-value child tables or hiding everything inside an opaque JSON blob.

`Searching` no longer derives a PubMed query directly from `ResearchQuestion`. It loads the accepted plan and executes `ResearchPlan.SearchQueries` sequentially against the existing scientific source abstraction. Each `LiteratureSearch` can reference the originating `ResearchPlan`.

## Validation Rules

Application validation remains mandatory even when the provider enforces a schema. The accepted planner output must satisfy at least:

- the LLM-provided original question matches the authoritative stored question after whitespace normalization;
- at least one search query exists;
- no more than five search queries exist;
- each query is non-blank and at most 300 characters;
- duplicate queries are removed case-insensitively;
- list fields and optional text fields are bounded;
- preferred study types use supported labels;
- obvious stable study identifiers such as PMID or DOI are rejected in search queries.

The prompt and schema prohibit PMIDs, DOIs, invented paper titles, invented authors, effect sizes, sample sizes, confidence intervals, p-values, evidence grades, diagnoses, treatments, and scientific conclusions.

## Alternatives Considered

- Letting Application call OpenAI directly: rejected because provider details, API keys, HTTP payloads, and response shapes would leak into use-case orchestration.
- A broad universal AI SDK abstraction: rejected as premature. The current need is strict structured generation for one planning task.
- Regexing arbitrary prose into a plan: rejected because it is brittle and weakens the trust boundary.
- Persisting the entire plan as one JSON blob: rejected because run/question ownership and provenance should remain queryable.
- Creating many child tables for every plan list: rejected because the bounded arrays are simple metadata, not independently owned domain records yet.
- Generating PubMed queries directly from the raw question after planning exists: rejected because it bypasses the accepted plan and breaks provenance.
- Live OpenAI tests in the normal suite: rejected because normal tests should not depend on paid credentials, internet availability, or changing provider behavior.

## Consequences

The first AI component is isolated behind Application contracts and Infrastructure adapters. Adding a second provider later should primarily require a new Infrastructure adapter plus configuration/DI registration, not changes to `ResearchPlanner` or Domain.

Planning failures, OpenAI configuration failures, provider failures, malformed structured responses, and validation failures use the existing safe research-run failure path. Detailed provider errors are logged internally; the API exposes only the safe failure reason.

The current planner can improve scientific search strategy, but it still does not extract evidence, score quality, synthesize conclusions, diagnose, recommend treatment, run autonomous agent loops, or perform RAG.
