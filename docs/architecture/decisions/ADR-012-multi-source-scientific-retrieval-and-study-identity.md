# ADR-012: Multi-Source Scientific Retrieval And Study Identity

Date: 2026-09-02

Status: Accepted

## Context

MedResearch originally retrieved scientific metadata from PubMed only. The search architecture already separated `Study` as global publication identity from `LiteratureSearch` and `ResearchStudyDiscovery` as run/search provenance. Adding Europe PMC introduces overlapping records, additional stable identifiers such as PMCID, provider source/id identity, and provider-specific failure modes.

The system must preserve traceability when the same publication is found by multiple sources or queries. It must also avoid unsafe merges when external metadata conflicts.

## Decision

Application uses provider-neutral literature contracts and a single `ScientificLiteratureSearchCoordinator` to execute enabled scientific sources for each accepted plan query.

Each source/query execution creates its own `LiteratureSearch` row. Each persisted discovery links that source-specific search execution to one canonical `Study` through `ResearchStudyDiscovery`. Multiple source/query paths may point to the same `Study`.

Infrastructure now implements:

- `PubMedScientificLiteratureSource` for NCBI E-utilities.
- `EuropePmcScientificLiteratureSource` for the Europe PMC Articles REST API.

Europe PMC uses the official `/search` endpoint with `format=json`, `resultType=core`, bounded `pageSize`, and cursor pagination. The adapter maps only source-reported metadata into `ScientificStudyCandidate`.

`Study` identity is resolved using normalized stable identifiers only:

- PMID
- PMCID
- DOI

PMCID is stored as nullable `Study.Pmcid` with a filtered unique PostgreSQL index. Provider source/id is retained as discovery provenance and does not become canonical publication identity when stronger stable identifiers exist. Persistence acquires transaction-scoped PostgreSQL advisory locks for normalized stable identity keys before resolving existing Studies, so concurrent PubMed/Europe PMC upserts for the same identifiers serialize without holding locks across external HTTP calls.

If all incoming stable identifiers match the same existing `Study`, the Study is reused. Missing existing metadata may be enriched conservatively. Null incoming values never erase non-null existing values. Conflicting non-null metadata is not overwritten.

If incoming stable identifiers point to different persisted Studies, MedResearch treats the candidate as a hard identity conflict. It logs bounded diagnostics, skips the ambiguous discovery, preserves existing Studies, and continues with other candidates.

No title, author, year, fuzzy, or provider-order matching is used for automatic merges.

## Consequences

Multi-source retrieval can increase recall while preserving per-source provenance and one downstream work item per canonical Study per ResearchRun.

Some Europe PMC records without PMID, PMCID, or DOI are skipped even if they have titles. This is deliberate until a first-class provider-record identity model is needed.

Conflicts are currently observable through logs and search duplicate/conflict counts, not a dedicated provider-result diagnostics table.

Provider request limiting is local to the process. This is enough for the current monolith and conservative result bounds, but it is not a distributed quota coordinator.

Normal CI uses fake HTTP/provider tests. Live Europe PMC availability is verified only through an explicit opt-in smoke test outside the normal solution.
