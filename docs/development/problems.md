# Problems

## 2026-08-30

Date: 2026-08-30
Area: API template dependency
Problem: The default ASP.NET Core Web API template restored an OpenAPI package with a high-severity advisory.
Observed behavior: `dotnet new webapi` restored `Microsoft.OpenApi` transitively through template OpenAPI support and emitted advisory `NU1903`.
Root cause: The stock template included OpenAPI support that is not needed for the initial repository skeleton.
Decision / fix: Removed template OpenAPI registration and the OpenAPI package reference. Kept a minimal `/health` endpoint, later replaced by standard health checks.
Verification: `dotnet restore`, `dotnet build --no-restore`, and `dotnet test --no-build` completed successfully.
Remaining concerns: Reintroduce OpenAPI later only with a non-vulnerable package set and a concrete API documentation need.

Date: 2026-08-30
Area: EF Core tooling
Problem: Creating migrations with `MedResearch.Api` as the startup project failed because the startup project did not reference `Microsoft.EntityFrameworkCore.Design`.
Observed behavior: `dotnet ef migrations add InitialCreate` built successfully, then reported that the startup project must reference `Microsoft.EntityFrameworkCore.Design`.
Root cause: EF tooling loads design-time services from the startup project as well as the target project.
Decision / fix: Added `Microsoft.EntityFrameworkCore.Design` as a private design-time package reference to `MedResearch.Api` while keeping runtime persistence code in Infrastructure.
Verification: `dotnet ef migrations add InitialCreate` completed successfully afterward.
Remaining concerns: Keep design-time dependencies private and avoid moving persistence implementation into API.

Date: 2026-08-30
Area: Docker-backed integration tests
Problem: Docker CLI and Docker Desktop are installed, but the Docker engine is not reachable in this environment.
Observed behavior: `docker info` failed with a missing `dockerDesktopLinuxEngine` named pipe. `docker desktop start` hung without making the engine available. Testcontainers could not connect to `npipe://./pipe/docker_engine`. `docker compose up -d --build` failed because `//./pipe/dockerDesktopLinuxEngine` was missing.
Root cause: Docker Desktop daemon is stopped or misconfigured outside the repository.
Decision / fix: Kept PostgreSQL integration tests on Testcontainers and added `Xunit.SkippableFact` so they are explicitly skipped when Docker is unavailable. Did not switch to EF Core InMemory.
Verification: `dotnet test MedResearch.slnx --no-build` passes with PostgreSQL integration tests reported as skipped when Docker is unavailable.
Remaining concerns: Run the full integration suite, `docker compose up -d --build`, and `/health` verification again when Docker Desktop is running.

Date: 2026-08-30
Area: GitHub remote/push
Problem: The local repository initially could not be pushed to the configured GitHub remote.
Observed behavior: Early `git ls-remote --heads origin` and `git push -u origin main` attempts returned `Repository not found` for `https://github.com/DanDan919/MedResearch.git`. A later attempt failed with a network connection error to `github.com:443`.
Root cause: The repository or credentials were not ready at first, and the local environment later had a transient network failure.
Decision / fix: Kept the configured remote URL because it matched the requested owner/repository path. After explicit approval to export repository contents to the exact remote, `git push -u origin main` succeeded.
Verification: `origin/main` existed at `bf85aa5e30ac81d20b83ea071a010f9cf412c915`, and local `main` tracked `origin/main`.
Remaining concerns: None for the configured remote at the time of that verification.

## 2026-08-31

Date: 2026-08-31
Area: Evidence migration
Problem: EF warned that reshaping the placeholder `evidence` table may result in data loss.
Observed behavior: `dotnet ef migrations add AddSourceGroundedEvidenceExtraction` scaffolded a migration that drops placeholder `claim` and `confidence` columns and adds required run/extraction/source-grounding fields.
Root cause: The previous `Evidence` model was a deliberate placeholder without `ResearchRun`, extraction provenance, source scope, or supporting source text, so any rows in that shape cannot be faithfully upgraded into source-grounded evidence.
Decision / fix: Kept the migration additive in history and documented the incompatibility. Did not rewrite older migrations or silently invent missing provenance for old rows.
Verification: `dotnet build MedResearch.slnx --no-restore` and `dotnet test MedResearch.slnx --no-build` completed successfully after the model change; Docker-backed PostgreSQL tests remain skipped while Docker Desktop is unavailable.
Remaining concerns: If a non-development database contains placeholder `evidence` rows, decide an explicit data migration/archive policy before applying this migration.

Date: 2026-08-31
Area: API integration dependency injection
Problem: API integration tests failed service-provider validation after adding evidence evaluation options.
Observed behavior: `WebApplicationFactory` tests replace Infrastructure persistence with fakes, so the configured Infrastructure registration that normally supplies `EvidenceEvaluationOptions` was not present while `ScientificResearchStageExecutor` required it.
Root cause: Application orchestration services depended on an options object whose safe default was registered only by Infrastructure configuration binding.
Decision / fix: Registered safe default `EvidenceExtractionOptions` and `EvidenceEvaluationOptions` in Application DI, while Infrastructure still replaces them with configured singleton values when normal persistence registration is used.
Verification: `dotnet test MedResearch.slnx --no-build` completed successfully after the DI change, with Docker-backed PostgreSQL tests skipped while Docker Desktop is unavailable.
Remaining concerns: Keep future Application-level orchestration options independently constructible for test hosts that intentionally replace Infrastructure.

## 2026-09-01

Date: 2026-09-01
Area: Synthesis report persistence
Problem: The first implementation of report persistence accidentally mapped `potential_conflict_detected` from the `EvidenceTruncated` source-coverage flag.
Observed behavior: Code review of `EfResearchSynthesisStore` showed both constructor arguments using `result.SourceCoverage.EvidenceTruncated`, which would have lost persisted conflict provenance.
Root cause: Adjacent boolean constructor arguments had the same type and were easy to transpose.
Decision / fix: Changed persistence to pass `result.SourceCoverage.PotentialConflictDetected` explicitly and kept coverage reconstruction tests around persisted reports.
Verification: `dotnet build E:\MedResearch\MedResearch.slnx --no-restore` and `dotnet test E:\MedResearch\MedResearch.slnx` passed after the fix; Docker-backed PostgreSQL tests were skipped because the Docker engine is unavailable.
Remaining concerns: Consider using a named options/object mapping pattern if future report coverage fields grow further.
Date: 2026-09-01
Area: EF Core DbContextFactory dependency injection
Problem: API integration tests failed service-provider validation after adding a DbContext factory for worker heartbeat/recovery.
Observed behavior: `dotnet test` reported that singleton `IDbContextFactory<MedResearchDbContext>` could not consume scoped `DbContextOptions<MedResearchDbContext>`.
Root cause: `AddDbContext` registered scoped context options, while `AddDbContextFactory` defaulted to singleton lifetime.
Decision / fix: Registered the DbContext factory with scoped lifetime so heartbeat/recovery queue operations can create independent DbContext instances without violating service-provider validation.
Verification: `dotnet build E:\MedResearch\MedResearch.slnx --no-restore` and `dotnet test E:\MedResearch\MedResearch.slnx --no-build` passed locally after the fix, with Docker-backed PostgreSQL tests skipped because Docker Desktop is unavailable.
Remaining concerns: CI must run the Docker-backed PostgreSQL suite with Docker available to verify runtime SQL behavior.