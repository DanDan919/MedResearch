# Frontend API Gaps

The frontend uses backend APIs directly and documents missing backend capabilities instead of inventing local stand-ins.

## Resolved Run List

F2 adds a real paginated run-history endpoint:

- `GET /api/research?page=1&pageSize=20`
- optional exact `status` filter using backend `ResearchRunStatus` values.

The `/research` route now renders backend-backed research history. It does not synthesize local history from detail endpoints or browser state.

## Resolved Run Progress

F3 adds a persisted run-progress endpoint:

- `GET /api/research/{researchRunId}/progress`

The `/research/[id]` route now renders a backend-backed execution observatory with pipeline stages, persisted counters, processing lease state, timestamps, and safe failure state. It does not invent percentages, ETA, live provider status, or a failed stage that the backend has not persisted.

## Missing Study Browser

The backend persists `Study`, `LiteratureSearch`, and `ResearchStudyDiscovery`, but does not expose a study list/search/detail API for the frontend. The `/studies` route renders an honest API-gap state.

## Missing Report List

Reports are retrievable by research run id only. There is no report index endpoint.

## Resolved Report Traceability Projection

F4 adds a report workspace backed by the existing `GET /api/research/{researchRunId}/report` endpoint. The response now projects persisted claim-to-Evidence links, authoritative Study identifiers and metadata, and SourceMaterial lineage metadata without exposing raw source content.

Still intentionally not exposed by this endpoint:

- a standalone provenance explorer for every search/discovery path;
- raw SourceMaterial content or full-text browsing;
- an automatic Evidence-to-EvidenceEvaluation projection when no direct report citation association exists;
- a full Evidence & Provenance Explorer; F7 only exposes quantitative contribution lineage IDs already present in the F6 artifact.

## Resolved Quantitative Results Projection

F6 provides `GET /api/research/{researchRunId}/quantitative`. F7 consumes it directly and renders persisted per-group common/fixed, random-effects, heterogeneity, HKSJ, prediction, contribution, and reproducibility fields.

The F6 artifact does not include publication titles, PMID/PMCID/DOI, author metadata, or contribution-level confidence intervals. F7 keeps those values unavailable instead of joining every contribution or calculating missing intervals in the browser. A future human-readable provenance view needs a dedicated bounded read model.

## Missing OpenAPI CI Source Of Truth

The frontend contains a checked-in OpenAPI snapshot in `frontend/packages/api/openapi/medresearch-api.json`. The backend now exposes `/openapi/v1.json`, but CI currently validates generation from the snapshot to keep frontend validation deterministic without starting the API. A future milestone can add a backend-generated OpenAPI artifact check if the API contract becomes part of release governance.

## Not Frontend Responsibilities

The frontend must not add local replacements for backend scientific behavior:

- no local evidence extraction;
- no local evidence evaluation;
- no local synthesis;
- no local quantitative calculations;
- no direct OpenAI/PubMed/Europe PMC calls.
