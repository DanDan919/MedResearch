# Frontend Agent Rules

1. The ASP.NET Core API is the source of truth for scientific and statistical behavior.
2. Never duplicate quantitative algorithms, evidence extraction, evaluation, synthesis, REML, Wald, HKSJ, or prediction interval calculations in frontend code.
3. Generated API contracts under `packages/api/src/generated` must not be manually edited.
4. Production UI must not show fake studies, evidence, reports, claims, effect sizes, or research history.
5. Web and desktop entry points should share API and UI packages where practical.
6. Tauri is a shell around the React UI; do not implement MedResearch backend behavior in Rust.
7. Run frontend lint, typecheck, tests, and build before completion.
