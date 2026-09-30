# MedResearch Frontend

The frontend workspace contains the browser and desktop foundations for MedResearch.

- `apps/web`: Next.js web application.
- `apps/desktop`: Tauri 2 desktop shell using the shared React UI/data packages.
- `packages/api`: generated OpenAPI types plus a small typed API client.
- `packages/ui`: shared UI primitives and layout helpers.

The web app includes backend-backed research creation, research run history, run details, a traceable report workspace, and a quantitative results workspace at `/research/{id}/quantitative`. The report view renders persisted claims, Evidence, Study identifiers, and source-material lineage metadata without exposing raw source content. The quantitative workspace reads the immutable F6 artifact, displays pooled outputs, heterogeneity, inference, prediction intervals, contribution snapshots, and lineage IDs, but performs no scientific calculations in the browser.

The quantitative endpoint can return multiple analysis groups. The UI keeps them run-scoped and selectable. F6 does not persist study titles or study-level confidence intervals inside the artifact, so the workspace does not invent those fields or derive them from standard errors.

See `docs/frontend/development.md` for verified commands.
