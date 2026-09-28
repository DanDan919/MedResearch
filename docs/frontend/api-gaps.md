# Frontend API Gaps

F1 wires the frontend only to existing backend behavior. Missing capabilities below are intentionally documented instead of implemented in this frontend foundation milestone.

## Missing Run List

The backend exposes:

- `POST /api/research`
- `GET /api/research/{researchRunId}`
- `GET /api/research/{researchRunId}/report`

It does not expose a paginated research-run list. The `/research` route therefore renders an honest API-gap state and links to `/research/new`.

## Missing Study Browser

The backend persists `Study`, `LiteratureSearch`, and `ResearchStudyDiscovery`, but does not expose a study list/search/detail API for the frontend. The `/studies` route renders an honest API-gap state.

## Missing Report List

Reports are retrievable by research run id only. There is no report index endpoint.

## Missing OpenAPI CI Source Of Truth

The frontend contains a checked-in OpenAPI snapshot in `frontend/packages/api/openapi/medresearch-api.json`. The backend now exposes `/openapi/v1.json`, but CI currently validates generation from the snapshot to keep frontend validation deterministic without starting the API. A future milestone can add a backend-generated OpenAPI artifact check if the API contract becomes part of release governance.

## Not Gaps For F1

The frontend must not add local replacements for backend scientific behavior:

- no local evidence extraction;
- no local evidence evaluation;
- no local synthesis;
- no local quantitative calculations;
- no direct OpenAI/PubMed/Europe PMC calls.
