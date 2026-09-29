# MedResearch Frontend

The frontend workspace contains the browser and desktop foundations for MedResearch.

- `apps/web`: Next.js web application.
- `apps/desktop`: Tauri 2 desktop shell using the shared React UI/data packages.
- `packages/api`: generated OpenAPI types plus a small typed API client.
- `packages/ui`: shared UI primitives and layout helpers.

The web app includes backend-backed research creation, research run history, run details, and a traceable report workspace. The report view renders persisted claims, Evidence, Study identifiers, and source-material lineage metadata without exposing raw source content. It does not perform scientific extraction, evaluation, synthesis, or quantitative calculations in the browser.

See `docs/frontend/development.md` for verified commands.
