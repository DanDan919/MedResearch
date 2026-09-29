# MedResearch Frontend Architecture

The frontend workspace presents MedResearch backend state without moving scientific responsibility out of the backend.

## Workspace

```text
frontend/
  apps/
    web/        Next.js React application
    desktop/    Tauri 2 shell with React/Vite UI
  packages/
    api/        OpenAPI-derived TypeScript types and typed transport client
    ui/         shared React primitives
```

The backend remains the source of truth for research lifecycle, evidence extraction, evaluation, synthesis, and all quantitative/statistical calculations. The frontend displays backend state and calls backend endpoints; it does not recompute evidence, HKSJ, prediction intervals, pooled estimates, or any other scientific result.

## API Boundary

The shared `@medresearch/api` package owns:

- generated OpenAPI TypeScript contracts in `packages/api/src/generated/`;
- Zod runtime validation for response payloads used by the UI;
- `MedResearchApiClient`, a small typed fetch wrapper;
- query keys and research-run status helpers.

React components do not scatter raw `fetch` calls. Web UI uses TanStack Query hooks in `apps/web/lib/api.ts`.

## Web App

The web app uses Next.js App Router, React, TypeScript, Tailwind CSS, TanStack Query, Zod, Lucide icons, and shared UI primitives.

Implemented routes:

- `/` dashboard and backend-boundary overview.
- `/research` real paginated run history through `GET /api/research`.
- `/research/new` real `POST /api/research` create flow.
- `/research/[id]` real `GET /api/research/{id}/progress` observability workspace with polling while non-terminal.
- `/research/[id]/report` traceable scientific report workspace through `GET /api/research/{id}/report`.
- `/studies` honest empty state because no study-browsing API exists.
- `/settings` environment configuration summary.

The app shell includes responsive navigation, light/dark theme support, an API readiness indicator, loading states, and typed error states.

## Desktop App

The desktop foundation is a Tauri 2 shell around a React/Vite UI. Rust is intentionally minimal and contains no scientific or business logic. The shell does not request shell or filesystem permissions.

The current desktop app verifies the configured API readiness endpoint and documents that backend services own all research processing. Future desktop milestones can reuse the shared API and UI packages rather than duplicating backend behavior.

The report workspace treats the report endpoint as a persisted read model. It renders narrative, coverage facts, ordered claims, and expandable Evidence citations with authoritative Study metadata. Identifier links are created only for identifiers returned by the API. Source-material lineage exposes provider, retrieval, version, access, and section metadata, but never raw source content. A known run without a report is rendered as a not-ready state (409); an unknown run remains a not-found state (404).

## Backend-Facing Contract

The frontend consumes:

- `POST /api/research`
- `GET /api/research`
- `GET /api/research/{researchRunId}`
- `GET /api/research/{researchRunId}/progress`
- `GET /api/research/{researchRunId}/report`
- `GET /health/ready`

Run history is paginated and status-filtered by the backend. The run detail workspace polls the progress endpoint only while a returned run is non-terminal. The frontend displays persisted counters and lease state but does not estimate percentages, ETA, failed stage, provider activity, evidence quality, or scientific/statistical values locally.
