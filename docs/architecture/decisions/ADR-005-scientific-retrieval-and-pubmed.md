# ADR-005: Add Provider-Neutral Scientific Retrieval with PubMed as the First Source

Status: Accepted
Date: 2026-08-30

## Context

The background worker can advance a `ResearchRun` through deterministic stages, but `Searching` needs to retrieve real scientific literature metadata. The first supported source is PubMed through official NCBI APIs. This milestone must not introduce AI planning, LLM extraction, embeddings, RAG, diagnosis, treatment recommendations, or fake scientific data.

External source data is untrusted. Missing PubMed metadata must remain missing, and the system must distinguish metadata reported by a source from future system inference.

## Decision

Define provider-neutral Application contracts for scientific retrieval:

- `IScientificLiteratureSource` searches a source using scientific search semantics and returns normalized `ScientificStudyCandidate` records.
- `IScientificSearchQueryBuilder` converts the current research question into a deterministic bounded query for now.
- `IScientificSearchResultStore` persists search provenance, normalized studies, and run-to-study discovery links.

Implement only PubMed in Infrastructure. PubMed-specific HTTP endpoints, ESearch JSON parsing, EFetch XML parsing, API key handling, and HttpClient configuration remain in Infrastructure.

Use official NCBI E-utilities:

- `esearch.fcgi` with `db=pubmed` to retrieve a bounded PMID list.
- `efetch.fcgi` with `db=pubmed` and `retmode=xml` to retrieve article metadata.

Use `IHttpClientFactory` through a typed PubMed client. Support configuration for base URL, result limit, timeout, tool name, optional email, optional API key, and request interval. Do not commit API keys or real secrets.

The development default result limit is 10. This keeps local runs bounded while preserving the ability to tune by configuration.

Extend `Study` only for useful metadata currently returned by PubMed and likely needed by future extraction:

- PMID
- DOI
- title
- abstract
- journal
- publication date plus year/month/day parts for incomplete dates
- publication types
- authors
- source

Add `LiteratureSearch` for search provenance and `ResearchStudyDiscovery` as the link between a `ResearchRun`, one search execution, and a global `Study`.

Treat `Study` as global scientific identity. Deduplicate by stable identifiers, preferring PMID when present and DOI where appropriate. Do not use fuzzy title matching in this milestone. Add filtered unique database indexes for PMID and DOI to prevent duplicate globally identifiable studies.

## Alternatives Considered

- PubMed-specific contracts in Application: rejected because future sources such as Europe PMC, OpenAlex, Crossref, and Semantic Scholar should not require redesigning Application orchestration.
- Scraping PubMed HTML: rejected because official NCBI APIs exist and are the appropriate integration surface.
- A universal scientific query language: rejected as premature. The current Application request contains a bounded query string and can evolve when a Research Planner exists.
- Empty placeholder implementations for future providers: rejected because they add noise without working behavior.
- Title-based deduplication: rejected because it can silently merge distinct records.
- Live PubMed tests in the normal suite: rejected because normal automated tests should not depend on internet availability or changing search rankings.

## Consequences

The `Searching` stage now performs real PubMed retrieval and persists normalized study metadata, search provenance, and discovery links. Future scientific sources can implement `IScientificLiteratureSource` without exposing provider transport details to Application.

The deterministic query builder is intentionally simple and not scientifically optimal. A future AI Research Planner can replace it with structured search planning while remaining separate from source adapters and LLM adapters.

Rate handling is conservative and local to the PubMed adapter. There is no distributed rate limiter yet. Automatic retries are not implemented in this milestone; source failures follow the existing safe run failure path.
