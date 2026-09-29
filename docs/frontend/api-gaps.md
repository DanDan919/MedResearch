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

## Missing OpenAPI CI Source Of Truth

The frontend contains a checked-in OpenAPI snapshot in `frontend/packages/api/openapi/medresearch-api.json`. The backend now exposes `/openapi/v1.json`, but CI currently validates generation from the snapshot to keep frontend validation deterministic without starting the API. A future milestone can add a backend-generated OpenAPI artifact check if the API contract becomes part of release governance.

## Not Frontend Responsibilities

The frontend must not add local replacements for backend scientific behavior:

- no local evidence extraction;
- no local evidence evaluation;
- no local synthesis;
- no local quantitative calculations;
- no direct OpenAI/PubMed/Europe PMC calls.
