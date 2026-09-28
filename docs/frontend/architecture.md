# MedResearch Frontend Architecture

Milestone F1 introduces a frontend workspace without moving scientific responsibility out of the backend.

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

Implemented F1 routes:

- `/` dashboard and backend-boundary overview.
- `/research/new` real `POST /api/research` create flow.
- `/research/[id]` real `GET /api/research/{id}` status view with polling while non-terminal.
- `/research/[id]/report` minimal report projection through `GET /api/research/{id}/report`.
- `/research` honest empty state because no run-list API exists.
- `/studies` honest empty state because no study-browsing API exists.
- `/settings` environment configuration summary.

The app shell includes responsive navigation, light/dark theme support, an API readiness indicator, loading states, and typed error states.

## Desktop App

The desktop foundation is a Tauri 2 shell around a React/Vite UI. Rust is intentionally minimal and contains no scientific or business logic. The shell does not request shell or filesystem permissions.

The current desktop app verifies the configured API readiness endpoint and documents that backend services own all research processing. Future desktop milestones can reuse the shared API and UI packages rather than duplicating backend behavior.

## Backend Changes

F1 adds only small frontend-enabling backend configuration:

- ASP.NET Core OpenAPI endpoint via `MapOpenApi`.
- development CORS origins for the local web and Tauri dev servers.

No research, persistence, statistical, or external-provider behavior changes are introduced by F1.
