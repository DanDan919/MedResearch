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

## 2026-09-02

Date: 2026-09-02
Area: CI PostgreSQL diagnostics
Problem: A green test command can hide lost PostgreSQL confidence if Docker-required tests are unexpectedly skipped.
Observed behavior: GitHub Actions already ran the Testcontainers suite successfully, but the workflow only surfaced TRX counters as notices and did not independently fail on nonzero skipped tests.
Root cause: The Testcontainers fixture correctly fails Docker unavailability when `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true`, but future accidental skips could still appear only as TRX metadata.
Decision / fix: Tightened the CI TRX parsing step so any skipped tests fail the workflow when Docker-backed tests are required.
Verification: GitHub Actions run `33583825095` completed successfully after the audit commit; the TRX guard reported 0 skipped Docker-required integration tests.
Remaining concerns: If future CI intentionally skips non-Docker tests, split the guard by test category instead of allowing broad skips.

Date: 2026-09-02
Area: Architecture documentation
Problem: `ARCHITECTURE.md` still listed crash recovery as missing after lease-based reclaim and fencing had been implemented.
Observed behavior: The worker code uses lease owner, expiry, heartbeat, and monotonically increasing lease version, but Current Limitations still said no automatic recovery existed.
Root cause: Documentation lag after the worker recovery milestone.
Decision / fix: Updated architecture and current-state documentation to describe stage-level lease recovery accurately and keep exactly-once external work out of scope.
Verification: Documentation review during the architecture audit.
Remaining concerns: Keep future ADR/current-state updates synchronized when worker semantics change.

Date: 2026-09-02
Area: Report citation graph trust boundary
Problem: PostgreSQL FKs prove report citations reference existing Evidence rows, but they do not by themselves prove the cited Evidence belongs to the same ResearchRun as the report.
Observed behavior: Application synthesis validation enforces same-run EvidenceIds before persistence, and synthesis corpus loading scopes Evidence to the current run.
Root cause: A cross-table same-run invariant would require redundant run ids, triggers, or a different join table shape beyond the current simple claim-to-evidence FK graph.
Decision / fix: Added a PostgreSQL integration test reconstructing the report claim/evidence/study graph for a shared Study and documented that Application validation is the trust boundary for same-run citation scope.
Verification: `dotnet test E:\MedResearch\MedResearch.slnx --no-build` passed locally after this audit change, with Docker-backed PostgreSQL tests skipped because Docker Desktop is unavailable. GitHub Actions run `33583825095` also completed successfully for the original report graph audit coverage.
Remaining concerns: Revisit database-level enforcement if unvalidated report persistence paths are introduced.
Date: 2026-09-02
Area: Search provenance modeling
Problem: A Study discovered by two different searches in the same ResearchRun could not preserve both discovery paths.
Observed behavior: `research_study_discoveries` had a unique `(research_run_id, study_id)` index, and `EfScientificSearchResultStore` skipped adding a second discovery row once a run/study pair existed.
Root cause: The schema mixed two different invariants: Study work should be deduplicated per run, but provenance should remain per search execution.
Decision / fix: Changed discovery uniqueness to `(literature_search_id, study_id)`, kept a non-unique `(research_run_id, study_id)` lookup index, and deduplicated extraction/synthesis study work by StudyId while preserving multiple LiteratureSearch/discovery rows.
Verification: `dotnet restore E:\MedResearch\MedResearch.slnx`, `dotnet build E:\MedResearch\MedResearch.slnx --no-restore`, `dotnet test E:\MedResearch\MedResearch.slnx --no-build`, and `dotnet ef migrations has-pending-model-changes` passed locally after the forward migration. Docker-backed PostgreSQL execution is pending the next CI run because local Docker Desktop is unavailable.
Remaining concerns: If future reports need to expose per-query discovery paths directly, add a read model for discovery provenance instead of overloading synthesis Study snapshots.


Date: 2026-09-02
Area: PubMed E-utilities production hardening
Problem: The PubMed adapter used conservative manual request spacing, but it did not enforce documented NCBI rate ceilings through explicit configuration validation and had no bounded transient retry policy.
Observed behavior: 429, 5xx, network failures, and timeouts immediately failed the search path; configured request pacing was expressed as an interval rather than an official-policy-bounded request rate.
Root cause: The first PubMed milestone intentionally kept provider behavior simple and deferred production-grade retry/rate semantics.
Decision / fix: Added validated PubMed options, optional API-key-aware rate ceilings, a centralized token-bucket request gate, bounded transient retry with Retry-After support, and deterministic fake-HTTP tests.
Verification: `dotnet build E:\MedResearch\MedResearch.slnx --no-restore` and `dotnet test E:\MedResearch\MedResearch.slnx --no-build` passed locally. GitHub Actions run `33613213245` completed successfully for commit `d21379542c605846b34168535877ad9207590bef`.
Remaining concerns: Rate limiting remains local to one process and is not a distributed NCBI quota coordinator.

Date: 2026-09-02
Area: PubMed EFetch batching
Problem: The adapter sent one EFetch request containing the full returned PMID list, without an explicit fetch batch size.
Observed behavior: Current small defaults kept this bounded, but the code did not prove or document 3/3/1-style batching behavior for larger configured result windows.
Root cause: The initial retrieval implementation optimized for the smallest working PubMed flow rather than production retrieval shape.
Decision / fix: Added `PubMed:FetchBatchSize`, chunked EFetch requests, deduplicated duplicate ESearch PMIDs before fetch, and deduplicated duplicate EFetch article records before returning provider-neutral candidates.
Verification: Deterministic fake-HTTP tests cover 7 PMID with batch size 3, zero-PMID no-fetch behavior, and duplicate ESearch/EFetch records.
Remaining concerns: History Server retrieval is deferred until result windows grow beyond small bounded direct PMID batches.