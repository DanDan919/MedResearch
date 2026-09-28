# Read-only adversarial hallucination and architecture audit

Date: 2026-09-28

Scope: MedResearch at `1f63f8a4081d56be5495bf656462f4d60beb433d`.

Mode: read-only audit. Production code, tests, migrations, configuration, and existing documentation were not modified. This document is the only audit artifact created after confirming the working tree was clean.

## 1. Starting state

- Branch: `main`.
- Remote: `https://github.com/DanDan919/MedResearch.git`.
- Tracking: `origin/main`.
- HEAD: `1f63f8a4081d56be5495bf656462f4d60beb433d`.
- HEAD message: `feat: add canonical HKSJ inference`.
- Working tree before audit: clean.
- Latest known successful CI supplied by prompt: `36075707736`.

Observed recent history:

```text
1f63f8a feat: add canonical HKSJ inference
902ad31 feat: add random-effects quantitative synthesis
2c98d69 test: audit architecture verification coverage
4d726e4 docs: add architecture comprehension guide
b1057fe feat: add REML tau-squared estimator
1be49b2 test: stabilize heterogeneity fake pipeline assertion
e6d7814 feat: add quantitative heterogeneity diagnostics
7b2ae86 feat: add fixed-effect quantitative synthesis
517de99 feat: add live scientific e2e validation harness
e673f2a test: use scenario-specific fake extraction draft
```

## 2. Classification scale

- `VERIFIED`: implemented in production code and exercised by relevant tests or validation.
- `TEST-ONLY`: guaranteed mainly by tests/fakes, not production runtime behavior.
- `DOC-ONLY`: documented intent without corresponding inspected enforcement.
- `STALE`: documentation or prompt names no longer match repository facts.
- `PARTIAL`: mostly true, but narrower than the claim or missing a lower-level invariant.
- `CONTRADICTED`: code shows the opposite.
- `HALLUCINATED`: no code, tests, docs, or config evidence.
- `UNKNOWN`: not established from inspected evidence.

Audit count:

```text
VERIFIED:      31
PARTIAL:        9
TEST-ONLY:      4
DOC-ONLY:       1
STALE:          4
UNKNOWN:        1
CONTRADICTED:   0
HALLUCINATED:   0
TOTAL:         50
```

## 3. High-signal findings

### Finding A - Provider failure provenance is operational, not durable

Classification: `PARTIAL`

Severity: medium.

Evidence:

- `ScientificLiteratureSearchCoordinator` separately executes each enabled source for each query.
- If at least one source succeeds, failures from other sources are logged and processing continues.
- `LiteratureSearch` rows are persisted only for successful source executions through `EfScientificSearchResultStore`.

Consequence:

The architecture preserves successful per-source/per-search provenance, but a source outage during a partially successful multi-source query is not stored as a durable `LiteratureSearch` failure record. Later synthesis/report coverage can show searched sources that succeeded, but cannot reconstruct every attempted provider failure from the database alone.

Suggested future fix:

Add explicit persisted source-search execution status if operational failure provenance must be queryable historically.

### Finding B - Same-run report citation guarantee is application-enforced, not schema-enforced

Classification: `PARTIAL`

Severity: medium.

Evidence:

- `ResearchReportDraftValidator` rejects claims referencing Evidence outside the supplied current-run `SynthesisContext`.
- `EfResearchSynthesisStore.LoadCorpusAsync` loads Evidence filtered by `researchRunId`.
- `research_report_claim_evidence` has FK to `Evidence`, but no composite DB constraint tying claim/report run to evidence run.

Consequence:

Normal application paths prevent cross-run report citations. Direct database writes or a future store bug would not be blocked by the relational schema alone.

Suggested future fix:

If defense-in-depth is desired, add a schema-level invariant or persistence check that binds report claim evidence to the report's `research_run_id`.

### Finding C - SourceMaterial current-version uniqueness is not a hard DB invariant

Classification: `PARTIAL`

Severity: medium-low.

Evidence:

- `EfSourceMaterialStore` marks current versions non-current and inserts a new current version in a transaction.
- EF config has unique `(study_id,type,provider,provider_source_id,content_hash)`.
- EF config has non-unique index `(study_id,type,is_current)`.
- No filtered unique index enforces one current material per `(study,type,provider,providerSourceId)`.

Consequence:

The code path aims to keep one current version per source identity, but the database does not fully enforce it under all possible concurrent writers.

Suggested future fix:

Consider a filtered unique PostgreSQL index for current source-material identity if concurrent full-text/source refresh becomes common.

### Finding D - Documentation contains small stale edges

Classification: `STALE`

Severity: low.

Observed stale or mismatched items:

- Prompt expected HKSJ under `ADR-019`, but actual HKSJ ADR is `ADR-020-canonical-hksj-summary-effect-inference.md`; `ADR-019` is REML/Wald random effects.
- `CODEX_CONTEXT.md` is not present in the repository root.
- `ARCHITECTURE.md` database index list still states unique `evidence_extractions(research_run_id, study_id, prompt_version)`, while current EF config includes `source_material_id` in the unique extraction idempotency key.
- `docs/development/current-state.md` migration bullet list omits `20260916032923_AddEvidenceQuantitativeStatistics`, although the same document later mentions it in the quantitative section.

Consequence:

The code is not contradicted, but onboarding readers may get small wrong details from docs or prompt carry-over.

## 4. Layer and dependency audit

Claim: Domain has no Application, Infrastructure, API, EF Core, ASP.NET Core, HTTP, Npgsql, OpenAI, or PubMed transport dependency.

Classification: `VERIFIED`.

Evidence:

- `MedResearch.Domain.csproj` has no MedResearch project references.
- `ArchitectureBoundaryTests.DomainProject_HasNoInfrastructureOrApplicationDependencies` checks project references and forbidden tokens.
- Inspected Domain entities contain business concepts and no persistence/HTTP APIs.

Claim: Application depends on Domain but not Infrastructure/API/EF/PostgreSQL/HTTP.

Classification: `VERIFIED`.

Evidence:

- `MedResearch.Application.csproj` references Domain only among MedResearch projects.
- `ArchitectureBoundaryTests.ApplicationProject_DependsOnlyOnDomainWithinMedResearch` checks forbidden tokens.
- Application ports include `IScientificLiteratureSource`, `IStructuredLlmClient`, stores, processors, validators, and quantitative read models.

Claim: Infrastructure owns EF Core, PostgreSQL, PubMed, Europe PMC, OpenAI adapter, and source material adapters.

Classification: `VERIFIED`.

Evidence:

- `MedResearch.Infrastructure.csproj` references Application and Domain.
- EF, Npgsql, Http, RateLimiting packages are in Infrastructure.
- PubMed, Europe PMC, OpenAI, persistence, source-material implementations live under Infrastructure.

Claim: API is composition/transport only.

Classification: `VERIFIED`.

Evidence:

- `Program.cs` registers `AddApplication()` and `AddInfrastructure(configuration)`.
- Endpoints call use cases and map DTOs/problem details.
- No EF or scientific pipeline logic was found in API endpoints.

## 5. Request lifecycle reconstruction

Actual flow:

```text
POST /api/research
  -> CreateResearchUseCase
  -> IResearchStore/EfResearchStore
  -> ResearchQuestion + queued ResearchRun
  -> BackgroundResearchWorker
  -> ResearchRunProcessor
  -> IResearchRunQueue/PostgreSqlResearchRunQueue
  -> ScientificResearchStageExecutor
  -> Planning: ResearchPlanner -> IStructuredLlmClient -> ResearchPlanStore
  -> Searching: ScientificLiteratureSearchCoordinator -> PubMed/EuropePmc sources -> SearchResultStore
  -> Extracting: SourceMaterialAcquirer -> EvidenceExtractor -> EvidenceExtractionStore
  -> Evaluating: EvidenceEvaluator -> EvidenceEvaluationStore
  -> Synthesizing: SynthesisContextBuilder -> ResearchSynthesizer -> ResearchReportStore
  -> GET /api/research/{id}/report
```

Classification: `VERIFIED`.

Notes:

- API does not advance status itself.
- Worker owns stage progression.
- Report endpoint reads persisted report projection; citation identifiers come from `Study`, not the LLM.

## 6. ResearchRun lifecycle and leases

Claim: Legal primary lifecycle is:

```text
Queued -> Planning -> Searching -> Extracting -> Evaluating -> Synthesizing -> Completed
```

Classification: `VERIFIED`.

Evidence:

- `ResearchRun.StartPlanning`, `StartSearching`, `StartExtraction`, `StartEvaluation`, `StartSynthesis`, `Complete`.
- Invalid transitions throw.
- Domain tests include invalid transition matrix coverage.

Claim: terminal states clear active lease metadata.

Classification: `VERIFIED`.

Evidence:

- `Complete`, `Fail`, and `Cancel` call `ClearLease()`.

Claim: stale workers cannot renew, save progress, fail, or release after ownership transfers.

Classification: `VERIFIED`.

Evidence:

- Queue updates require `processing_lease_owner` and `processing_lease_version`.
- PostgreSQL integration tests cover stale renew/save/fail/release.

Claim: claim/reclaim is atomic and uses PostgreSQL semantics.

Classification: `VERIFIED`.

Evidence:

- `PostgreSqlResearchRunQueue` uses a transaction and `FOR UPDATE SKIP LOCKED`.
- Reclaim preserves current stage for active statuses.

Claim: exactly-once external work is guaranteed.

Classification: `DOC-ONLY` as a negative claim: this is deliberately not promised.

Evidence:

- `ARCHITECTURE.md` states lease recovery is stage-level retry/resume rather than exactly-once external work.

## 7. Scientific retrieval and provenance

Claim: one planned query against PubMed and Europe PMC creates two source-specific `LiteratureSearch` rows.

Classification: `VERIFIED`.

Evidence:

- `ScientificLiteratureSearchCoordinator` loops sources per query with a new `SearchExecutionId`.
- `EfScientificSearchResultStore` creates one `LiteratureSearch` per persistence request.
- Integration tests cover PubMed+EuropePmc same-study source-specific discovery paths.

Claim: `ResearchStudyDiscovery` uniqueness is `(literature_search_id, study_id)`, not `(research_run_id, study_id)`.

Classification: `VERIFIED`.

Evidence:

- `ResearchStudyDiscoveryConfiguration` has unique index `ux_research_study_discoveries_literature_search_id_study_id`.
- Multiple discovery path tests exist.

Claim: zero-result searches are valid scientific results.

Classification: `VERIFIED`.

Evidence:

- Search persistence stores `LiteratureSearch` with `ResultCount` and zero candidates.
- Search coordinator treats an empty candidate set from a source as success.

Claim: provider-neutral Application boundary hides PubMed/Europe PMC transport DTOs.

Classification: `VERIFIED`.

Evidence:

- Application contracts use `ScientificSearchRequest`, `ScientificSearchResult`, `ScientificStudyCandidate`.
- PubMed XML/JSON and Europe PMC JSON parsing remain Infrastructure concerns.

## 8. Study identity and metadata merge

Claim: stable identity uses normalized PMID, PMCID, DOI only; no title/fuzzy merge.

Classification: `VERIFIED`.

Evidence:

- `ScientificIdentifierNormalizer` handles PMID/PMCID/DOI.
- `EfScientificSearchResultStore.ResolveExistingStudyAsync` matches only PMID, PMCID, DOI.
- Candidates without stable IDs remain separate rows in tests.

Claim: hard identifier conflicts are not silently merged.

Classification: `VERIFIED`.

Evidence:

- Multiple matches produce `StudyResolution.Conflict`.
- Conflicts are logged and skipped.
- Integration tests cover stable identifiers pointing at different studies.

Claim: metadata merge never overwrites existing richer non-null values.

Classification: `VERIFIED`.

Evidence:

- `Study.EnrichMissingMetadata` uses null-coalescing assignment for scalar fields and merge-distinct for arrays.

Claim: concurrency-safe upsert is protected by database authority.

Classification: `VERIFIED`.

Evidence:

- Transaction-scoped advisory locks over normalized identity keys.
- Filtered unique indexes on DOI, PMID, PMCID.
- PostgreSQL concurrent upsert integration test.

## 9. Source material and evidence lineage

Claim: `SourceMaterial` stores immutable extraction source snapshots.

Classification: `VERIFIED`.

Evidence:

- Source material has content hash, version, current flag, retrieval provenance, access status, truncation, sections.
- Same content reuses; changed content creates a new version.

Claim: completed `EvidenceExtraction` must retain exact `SourceMaterialId`.

Classification: `VERIFIED`.

Evidence:

- Domain constructor rejects completed extraction with null `SourceMaterialId`.
- `EfEvidenceExtractionStore` validates source material belongs to extraction study.

Claim: extraction validates supporting excerpts against source text.

Classification: `VERIFIED`.

Evidence:

- `EvidenceGroundingValidator` normalizes and requires containment.
- `EvidenceExtractionDraftValidator` rejects ungrounded supporting text.

Claim: all extracted scientific text fields are source-grounded.

Classification: `PARTIAL`.

Evidence:

- `supportingText` is source-grounded.
- numeric values are only kept if source-grounded.
- optional text fields such as population/intervention/comparator are length-normalized but not individually containment-checked.

Consequence:

The authoritative finding support is grounded; auxiliary structured descriptors remain LLM-derived and bounded.

## 10. Evaluation boundary

Claim: Evidence evaluation is source-aware and not a formal GRADE/RoB framework.

Classification: `VERIFIED`.

Evidence:

- Evaluation prompt/validator and `AGENTS.md` explicitly forbid formal framework claims.
- Domain stores internal categorical fields and no numeric quality score.

Claim: absence of source detail must not be converted into a negative quality judgment.

Classification: `VERIFIED`.

Evidence:

- `EvidenceEvaluationDraftValidator.RejectAbsenceAsConcern`.
- Abstract source rules convert unavailable detail to `InsufficientSource`/`NotApplicable`.

Claim: evaluation uses only same-run grounded evidence.

Classification: `VERIFIED`.

Evidence:

- `EfEvidenceEvaluationStore` queries evidence by `researchRunId`.
- Validator rejects ungrounded evidence.

## 11. Evidence corpus and synthesis

Claim: `EvidenceCorpusBuilder` is a trust boundary for current-run synthesis.

Classification: `VERIFIED`.

Evidence:

- Tests reject cross-run evidence and incoherent source lineage.
- `SynthesisContextBuilder` consumes the validated corpus and filters selected evidence/studies by current run.

Claim: the LLM cannot provide authoritative citation IDs.

Classification: `VERIFIED`.

Evidence:

- `ResearchReportDraftValidator` rejects model-supplied PMID/DOI/StudyId.
- Claims must cite supplied `EvidenceId` values.
- Report projection reconstructs PMID/PMCID/DOI from `Study`.

Claim: no validated evidence causes deterministic insufficient-evidence report without LLM call.

Classification: `VERIFIED`.

Evidence:

- `ResearchReportDraftValidator.CreateInsufficientEvidenceResult`.
- Tests cover insufficient evidence behavior.

## 12. Quantitative chain M17-M23

Claim: fixed/common-effect synthesis is deterministic Application code over eligible compatible OR/RR/HR evidence.

Classification: `VERIFIED`.

Evidence:

- `FixedEffectQuantitativeStatisticalSynthesizer`.
- Tests cover log scale, inverse-variance weights, CI, unsupported measures, invalid inputs.

Claim: Q/df/I-squared use the same fixed-effect contribution population and weights.

Classification: `VERIFIED`.

Evidence:

- `HeterogeneityDiagnosticsCalculator` consumes contributions and pooled analysis-scale effect.
- Tests cover analysis-scale Q, df, I-squared zero bound, order independence, invalid inputs.

Claim: REML tau-squared is foundation data, not automatic model selection.

Classification: `VERIFIED`.

Evidence:

- `RestrictedMaximumLikelihoodTauSquaredEstimator` is pure Application code.
- ADR-018/current docs state no automatic model selection.

Claim: M22 random-effects result reuses M19 tau-squared and leaves M17 fixed-effect unchanged.

Classification: `VERIFIED`.

Evidence:

- `RandomEffectsQuantitativeStatisticalSynthesizer` consumes `BetweenStudyVarianceEstimate`.
- Result is nested beside fixed/common-effect result.

Claim: M23 canonical HKSJ reuses M22 RE point estimate/weights, uses `df = k - 1`, Student-t CI, and is beside Wald.

Classification: `VERIFIED`.

Evidence:

- `HksjSummaryEffectInferenceCalculator`.
- `QuantitativeRandomEffectsSynthesisResult.HksjInference`.
- Tests compare BCG reference values to `metafor test="knha"`.
- Tests cover k=1, k=2, tau²=0, positive tau², order independence, non-finite contribution input.

Claim: LLM does not calculate quantitative results.

Classification: `VERIFIED`.

Evidence:

- Quantitative classes are Application-only deterministic read models.
- `SynthesisContextBuilder` maps results before LLM synthesis.
- Documentation/prompt contracts forbid LLM calculation or alteration.

## 13. Persistence and migrations

Claim: EF Core mapping uses explicit configurations.

Classification: `VERIFIED`.

Evidence:

- `MedResearchDbContext` uses `ApplyConfigurationsFromAssembly`.
- Configurations exist for all inspected entities.

Claim: fresh PostgreSQL migration application is tested.

Classification: `VERIFIED` in CI / `TEST-ONLY` locally.

Evidence:

- `MigrationRuntimeTests.EfMigrations_ApplyToFreshPostgreSqlDatabase`.
- Local Docker unavailable, so this test skips locally.
- CI is configured to require Docker-backed tests and fail skipped tests.

Claim: current model has no pending EF migration.

Classification: `VERIFIED`.

Validation:

- `dotnet ef migrations has-pending-model-changes` passed: no pending model changes.

## 14. Tests and CI

Normal solution projects:

- `MedResearch.Domain.Tests`
- `MedResearch.Application.Tests`
- `MedResearch.Infrastructure.Tests`
- `MedResearch.IntegrationTests`

Live opt-in projects exist but are outside `MedResearch.slnx`:

- `MedResearch.LiveE2EValidationTests`
- `MedResearch.LivePubMedSmokeTests`
- `MedResearch.LiveEuropePmcSmokeTests`
- `MedResearch.LiveEuropePmcFullTextSmokeTests`

Claim: normal CI runs deterministic tests, requires Docker/PostgreSQL Testcontainers, and does not run live PubMed/Europe PMC/OpenAI tests.

Classification: `VERIFIED`.

Evidence:

- `.github/workflows/ci.yml` runs four normal test projects.
- `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true`.
- TRX parser fails workflow when skipped tests occur under required-Docker mode.
- Live projects are not in `MedResearch.slnx` or CI test list.

Local test result on this audit machine:

```text
Domain:         25 passed, 0 failed, 0 skipped
Application:   145 passed, 0 failed, 0 skipped
Infrastructure: 65 passed, 0 failed, 0 skipped
Integration:    9 passed, 0 failed, 62 skipped
Total normal:  244 passed, 0 failed, 62 skipped
```

Skip reason:

- local Docker Desktop Linux engine unavailable.

## 15. Health, Docker, configuration, security

Claim: `/health/live` is liveness without external provider dependency.

Classification: `VERIFIED`.

Evidence:

- `Program.cs` maps `/health/live` with predicate false.

Claim: `/health/ready` checks PostgreSQL but not OpenAI/PubMed/Europe PMC.

Classification: `VERIFIED`.

Evidence:

- readiness predicate includes `ready`/`database` checks.
- Infrastructure adds DbContext health check tagged `database`, `postgresql`, `ready`.

Claim: app can start without OpenAI API key.

Classification: `PARTIAL`.

Evidence:

- OpenAI options allow null `ApiKey`.
- Startup does not validate model/key.
- Real LLM pipeline invocation will fail clearly if provider config is missing.

Claim: no production secrets are committed.

Classification: `VERIFIED` for inspected config.

Evidence:

- `.env.example` contains development defaults/placeholders only.
- Compose uses development PostgreSQL default password and empty OpenAI/PubMed secrets.

## 16. Validation commands

Executed locally:

```text
dotnet restore MedResearch.slnx
dotnet build MedResearch.slnx --no-restore
dotnet test MedResearch.slnx --no-build
dotnet ef migrations has-pending-model-changes --project src/MedResearch.Infrastructure/MedResearch.Infrastructure.csproj --startup-project src/MedResearch.Api/MedResearch.Api.csproj
docker compose config
git diff --check
docker info
```

Results:

```text
restore: passed
build: passed, 0 warnings, 0 errors
test: passed with expected local PostgreSQL skips
EF pending model: passed, no pending model changes
docker compose config: passed
git diff --check: passed
docker info: failed locally; Docker daemon pipe unavailable
```

Docker error summary:

```text
failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine
```

No live OpenAI, PubMed, Europe PMC, or full-text smoke tests were run.

## 17. Overall assessment

The current architecture is mostly real, not hallucinated. The strongest verified guarantees are:

- clean layer direction with tests;
- run-owned worker lifecycle and PostgreSQL lease fencing;
- provider-neutral scientific retrieval with per-source successful provenance;
- stable identifier based `Study` identity;
- run-scoped Evidence/Evaluation/Report paths;
- source-grounded excerpts and numeric evidence filtering;
- deterministic quantitative chain through canonical HKSJ;
- CI enforcement that required PostgreSQL/Testcontainers tests must not skip.

The main gaps are not broad design failures. They are lower-level hardening edges:

- durable recording of failed provider attempts in partially successful multi-source searches;
- optional schema-level defense for same-run report citations;
- optional schema-level uniqueness for current source-material versions;
- small documentation drift around ADR numbering, migration list, and extraction idempotency index.

No `CONTRADICTED` or `HALLUCINATED` production architecture claims were found in the inspected current code. The system still has known intentional limitations: no formal GRADE/RoB, no model selection, no prediction intervals, no tau² CI, no RAG/vector search, no distributed provider rate limiter, no PubMed History Server, and no production migration strategy.

