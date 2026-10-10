# Current State

## Search Query Contract Correction (2026-10-11)

Starting HEAD `d14d4abeaccb74470473489e918699147f360066`, clean main.
A local Codex CLI run exposed PubMed-style queries sent unchanged to Europe PMC
and off-topic retrieved publications. Europe PMC now prepares supported
Title/Abstract/tiab and Title/ti operands into native fields before HTTP.
Unsupported/malformed fielded syntax fails closed and records a typed logical
attempt; new success/attempt provenance stores the actual executed query.
Historical successful keys and rows remain unchanged. Source selection is now
explicit in structured logs. No schema, planner prompt or scientific validator
was changed. The local run's PubMed was disabled by temporary configuration,
not demonstrated to be unavailable. Its report correctly abstained; retrieval
and grounding still do not establish universal question relevance. That gap
remains technical debt, not a claimed fix. A browser favicon 404 is corrected
with a local PNG response and browser regression coverage.

Local validation: restore/build passed with 0 warnings/errors; Domain 45,
Application 304, Infrastructure 136, Integration 52 passed/133 Docker skips,
0 failures. SDK 42/Web 122, browser 15 and synthetic production OIDC 17 passed;
lint/typecheck/production build, EF pending-model and Compose checks passed.
Implementation `d3b36ed91602646f49753318c4c05a981fb416a8` passed
[CI 38081961226](https://github.com/DanDan919/MedResearch/actions/runs/38081961226):
all three jobs succeeded. TRX: Domain 45, Application 304, Infrastructure 136,
Integration 185; 670 passed, 0 failed/skipped, including all 133 required
PostgreSQL cases. Fresh migrations, EF pending-model/Compose, deterministic
frontend, actual API/PG/HTTPS browser and Docker image gates passed. No live
scientific provider or paid LLM call was added to normal tests. A read-only local
Chrome smoke of the preserved run after restart returned favicon 200 and zero
console/page errors. Post-fix live scientific relevance was NOT verified;
the existing reports/evidence were preserved, not regenerated.

## SAAS-003 Atomic Research Admission

Implemented bounded pilot admission in the existing create transaction with a
PostgreSQL transaction advisory lock, immutable daily reservations and required
owner-scoped UUID Idempotency-Key. Defaults owner/global outstanding 1/2 and daily
2/10; invalid configuration fails startup. PostgreSQL UTC daily accounting never
refunds terminal outcomes. Stop blocks only new work; all replicas need identical
startup configuration. Existing scientific stages/worker fencing are unchanged.
Backend-origin SDK/BFF/form are updated; legacy clients must supply the header.
SAAS-003 COMPLETE - DETERMINISTIC: implementation
`6f918b7dd69eff8401aa05b7098159135cb877ff`, CI `38078055334` SUCCESS.
642 backend cases passed, 0 failed/skipped, including 132 real PostgreSQL cases
and separate OS API-process admission tests. All frontend and actual API/PG/HTTPS
browser/container gates passed. Local Docker remains unavailable: 510 passed,
132 honest skips; SDK 42/Web 122, Chromium 15/17 passed locally. No live providers.
Read ADR-033 and `saas-003-atomic-research-admission-ru.md` for exact guarantees,
red-before-green evidence, UTC/replay/rollback tests and verification boundaries.
Not a money budget: SAAS-002 cost/lifetime recovery bounds remain unresolved;
Gate B/live scientific validation is not claimed.

## F19 Real Deployment Preparation

F19 PREPARED - AWAITING OPERATOR CONFIGURATION. Gate A is complete.
Implementation `d3f305d33ef183dd32687051ac2c0629efbbda92` passed CI `37933348386`.
Starting HEAD `1cebd49cfb8c07d7b20665f35ce8344b3e4c91cc`, clean main/origin/main.
Offline operator preflight reuses the actual web validator, checks effective API
alignment/disabled worker, and supports separately approved anonymous probes with
exact origin allowlists, bounded reads and no credentials/redirects. 40 fake-HTTP
controls passed; normal CI runs only these offline controls, never live preflight.
Current environment has no operator-provided production settings, hostname/IdP,
two accounts or external-action approval. Actual offline preflight fails safely
with network NOT RUN. Gate B is NOT RUN; no deployment or real identity proof is
claimed. See `f19-real-https-oidc-deployment-verification-ru.md` and the operator
runbook. Science/auth architecture/schema are unchanged.
CI: backend599 passed/0 failed/0 skipped, including all103 required PostgreSQL
cases; offline preflight40, security5, API40/Web99 and Chromium15/17/17 passed.
Actual API/PG synthetic-issuer browser regression, image builds, OpenAPI/EF/Compose
and 45 populated viewport scenarios remain green. These are not Gate B evidence.

## F18 Web Release Candidate

F18 COMPLETE - DETERMINISTIC WEB RC. Implementation HEAD
`1ecd40332de1e633a35916890546b29422babeb3` passed CI `37929197670` on
2026-10-09. Starting HEAD 39a9bd9963ddc41a53c3d1700a91d798ca1e20ab,
clean main/origin/main; history was not rewritten.
Next 16.3.8 and source-map-js 1.2.2 resolve the production audit advisories:
production 0; full audit retains one dev-only HIGH braces advisory without a
published patched version. Audit reports are unfiltered and production
HIGH/CRITICAL block CI.

Backend-origin OpenAPI comparison, TS/Zod alignment controls, mobile grid fix,
standalone packaging/operator guide and a real API/PostgreSQL browser harness
are implemented. Local Domain 45/Application 289/Infrastructure 110/Integration
52 passed; 103 integration cases skip only because Docker is unavailable.
Frontend API 40/web 99, security policy 5, scientific Chromium 15 and production
OIDC Chromium 17 passed. EF has no pending model change; Compose config passes.
The full-stack runner fails explicitly when local Docker is unavailable.
Transient database HTTP failures are now operational 503 with bounded generic
ProblemDetails, not validation 400; non-transient and ordinary validation controls
are separate. Full-stack outage injection pauses PostgreSQL without changing its
ephemeral endpoint. CI: Domain 45/Application 289/Infrastructure 110/Integration
155, all 599 passed, zero failures/skips. All 103 locally Docker-skipped test
names were matched to passed CI TRX results. Frontend API 40/web 99, security
policy 5, scientific Chromium 15 and synthetic production OIDC Chromium 17
passed. The separate actual API/PostgreSQL/trusted-HTTPS suite passed 17 cases,
zero skipped/flaky; its 45 populated route/viewport measurements showed zero
page-wide overflow. Both Docker images built; EF/Compose/drift gates passed.
See `f18-web-release-candidate-ru.md` for the evidence boundary and CI outcome.
External IdP/deployed HTTPS/native Tauri/live science remain NOT VERIFIED.

## F17 Production Web Authentication

Implemented configurable OIDC code/PKCE/state/nonce with pinned
openid-client/iron-session/jose, encrypted host-only HttpOnly browser sessions,
private same-origin Next.js BFF and protected workspace navigation. Access JWT is
independently validated by BFF and unchanged ASP.NET middleware. Owner identity
remains the configured issuer's stable sub. No refresh storage, scientific or
schema changes; no production DevelopmentLocal bypass. Logout/expiry/switch clear
scientific caches; cross-tab checks are bounded, not global revocation.

Local verification: Domain 45, Application 289, Infrastructure 110, Integration
36 passed/103 unavailable-Docker skips; zero failures. Frontend API 31/web 99,
scientific Chromium 14 and production synthetic OIDC Chromium 16 passed.
F17 COMPLETE - DETERMINISTIC. Feature commit
`d7ec8357dcd13f0f90f56183c696dc967678ccb1` passed CI `37788883185`:
Domain 45, Application 289, Infrastructure 110, Integration 139; all 583
passed with zero failures/skips. All 103 required PostgreSQL/Testcontainers
cases actually executed, including the new real signed-JWT owner isolation test.
EF model, Compose, frontend checks, both Chromium suites and desktop React/Vite
build passed. Local Docker pipe remains unavailable; native Tauri NOT RUN.
External IdP/live science NOT RUN. No refresh/global revocation or graceful
multi-key cookie rotation is claimed. Existing dependency advisories remain a
release follow-up. See F17 report and the authentication deployment guide.

## F16 Structured Narrative Claim Grounding

Baseline: `d5d3c5c02bccdbcd37b6ea7f36c9be02566bfe3c`, clean `main` tracking `origin/main`. Ten hostile free-text claims reproduced the old gap before the production fix: all ten expected rejection tests failed because the old validator accepted them.

Authoritative claims now use `structured-claim-v1` scope, closed kinds/directions, exact current-run Evidence references and optional grounded statistic/persisted artifact selectors. Backend validates and renders numbers/sentences; discarded model sections cannot override structured claims. Mixed support is not uniform effect; insufficiency is not no effect. Persistence revalidates, fences, preserves idempotency and exposes legacy claims as unverified. Forward migration: `20261008072325_AddStructuredReportClaims`. ADR-031 and the Russian F16 process report describe the actual limits.

F16 COMPLETE - DETERMINISTIC. Production commit `e20c2ff32b2a34324a071a0f4be7af2705d9f8f8` passed CI `37751521860`: Domain 45, Application 289, Infrastructure 110, Integration 128; all 572 passed, zero failures/skips. All 102 Docker-backed PostgreSQL cases actually executed, including fresh migrations, structured roundtrip/artifact linkage, bypass rejection, fencing and fake vertical E2E. EF pending-model, Compose, frontend, Chromium and desktop React/Vite jobs succeeded.

37 Application and 5 Domain controls and 9 PostgreSQL cases were added; fake E2E now covers Wald/HKSJ/PI artifact claims and GET report/provenance. Local Debug/Release each passed 470 tests with 102 expected unavailable-Docker skips. Frontend local API 31, web 33, Chromium 14 passed, including hydration. Native Tauri remains unverified. Live workflow is NOT RUN; six opt-in live cases deliberately skipped. The F16 process report records the exact boundary, evidence and remaining limitations; final verification-only Git/CI state is also reported at handoff.

## F15.2 Provider/Runtime Integrity

F15.2 removes Europe PMC indexing-date fallback from publication metadata and prevents incompatible Study date-part enrichment. It adds lease-fenced durable LiteratureProviderAttempt history (including zero results, failure, timeout, cancellation and unfinished Started). Successful attempt completion is atomic with scientific search output; existing successful search recovery keys remain authoritative. The owner/run-scoped provenance API and minimal Evidence workspace expose attempts. A forward migration, `20261008053910_AddLiteratureProviderAttempts`, adds metadata only.

PubMed ESearch/EFetch, Europe PMC search and structured full text use inclusive streaming byte caps and explicit cancellable body deadlines. Error bodies/URI logging are disabled; typed bounded diagnostics replace raw transcripts. F15.1 source-proof protections and M17-M24 formulas are unchanged. Normal automated tests use fake providers/HTTP; PostgreSQL verification is CI-authoritative when local Docker is unavailable. Detailed final verification is recorded in `f15-2-provider-runtime-integrity-hardening-ru.md`.

Production commit `bd1d285` was verified by CI `37735198597`: Domain 40,
Application 252, Infrastructure 110, Integration 119; all 521 passed with zero
failures/skips. All 93 Docker-backed PostgreSQL cases actually executed,
including fresh migration application and the new attempt/fencing tests.
EF model, Compose, frontend and Chromium jobs succeeded. Local Debug/Release
each passed 428 tests with 93 expected unavailable-Docker skips. Frontend local
API 24, web 29 and Playwright 12 passed, including hydration regression.
Six opt-in live cases deliberately skipped; no live scientific workflow ran.
Native Tauri remains unverified; desktop React/Vite build passed.

## F15.1 Scientific Trust Boundary Correction

Baseline after the separately authorized audit-document commit: `360e38a`.
The independent audit is retained unchanged. ADR-029 supersedes the disproved
sentence-level protection in ADR-028.

- Explicit signed measure/estimate tuples bind outcome, CI/confidence level,
  SE and p/operator under a conservative local grammar. Unknown or ambiguous
  relationships are not guessed; sample roles/scopes exclude hospital counts.
  Source-derived lexical case distinguishes bare OR from ordinary lowercase or;
  canonical historical offsets/hashes remain unchanged.
- Evidence has nullable bounded `Timepoint`, migration
  `20261008041640_AddEvidenceTimepoint`. Intervention, comparator and explicit
  timepoint are mandatory compatible quantitative dimensions; missing values
  are not wildcards. GroupKey semantics are versioned `estimand-v2`.
- Corpus/assessor recheck canonical membership against the immutable exact source
  content/hash and required persisted proof. Known normalization, lineage, scope,
  overlap uniqueness and confidence-level derivation proof are enforced.
- SynthesisContext uses revalidated corpus Evidence, not raw snapshot Evidence.
  Unverified statistics are nulled; raw ResultSummary is excluded from synthesis
  and evaluation provider prompts. Source quotation is not additional numeric
  authority. General numeric entailment of final free text remains unverified.
- Prompt versions: extractor `evidence-extractor-v3-bound-tuples`, evaluator
  `evidence-evaluator-v2-no-raw-summary`, synthesizer
  `research-synthesizer-v2-trusted-evidence`. F12 repair budgets, worker fencing,
  ownership and M17-M24 formulas are unchanged.
- Detailed red baseline, tests, CI outcome and residual limits:
  `f15-1-scientific-trust-boundary-correction-ru.md`.
- Corrected production HEAD `2aeebe1` verified by CI `37730308819`: Domain 32,
  Application 245, Infrastructure 73, Integration 107; all 457 passed with zero
  failures/skips. Docker-required PostgreSQL tests actually executed. Fresh
  migrations, EF model, Compose and frontend/browser jobs succeeded. Local Debug
  and Release each passed 376 tests with 81 unavailable-Docker skips. Live
  scientific workflow was NOT RUN; general final free-text entailment remains
  a documented limitation, not a guarantee inferred from green CI.

## F8 adversarial verification

The F8 audit is recorded in `docs/audits/f8-full-system-adversarial-verification-ru.md`. It found and fixed a stage-write fencing gap: the existing lease version previously protected ResearchRun lifecycle updates but not every scientific persistence store. Production DI now attaches a PostgreSQL `IResearchRunWriteFence` to the worker scope, and stage stores check owner/version, active status, and lease expiry inside their own short write transactions. No transaction spans external provider calls.

F8 also hardens quantitative exact SourceMaterial lineage, collision-safe compatibility GroupKeys, report-store citation validation, and consistency checks between persisted quantitative JSON and relational contribution snapshots. These changes add no schema migration or new scientific capability. Docker-backed regression tests remain authoritative in CI when local Docker is unavailable.

Date: 2026-09-29

The F5 full-system adversarial verification is recorded in
`docs/audits/f5-full-system-verification-ru.md`. It confirms the implemented
pipeline and explicitly labels application-only versus database-enforced
invariants. SourceMaterial constructors validate content hash/length, and
source-material version writers serialize updates with a PostgreSQL transaction
advisory lock.

## Exists Now

### F10 authentication and ownership boundary

- Production research routes use ASP.NET Core JWT Bearer authentication with
  configured issuer, audience, signature, and lifetime validation.
- The application consumes the immutable `sub` claim through `ICurrentActor`;
  HTTP headers and client-supplied owner IDs are not accepted as ownership
  input.
- `ResearchQuestion.OwnerSubjectId` is the immutable ownership root. History,
  run, progress, report, and quantitative reads apply owner scope in their
  application/store contracts and EF queries.
- `/health`, `/health/live`, and `/health/ready` remain anonymous. Research
  endpoints require the `AuthenticatedUser` policy. Unauthorized resources
  are returned as 404-style results after authentication to reduce existence
  disclosure.
- Local Compose development uses explicitly gated `DevelopmentLocal` auth;
  the mode is rejected outside `Development`. Integration tests use a
  test-only deterministic authentication handler and no external identity
  provider.
- Existing rows are migrated to `legacy-unowned` and are not silently exposed
  to the first authenticated user. Canonical `Study` and scientific
  `SourceMaterial` identity remain global; run-scoped scientific data remains
  protected through the owner-scoped run.

### F11 development Codex CLI provider

- `CodexCliStructuredLlmClient` implements the existing
  `IStructuredLlmClient` contract; Application stages remain unaware of the
  provider.
- The provider is selected with `AI:Provider=CodexCli` only in `Development` or
  `ManualScientificE2E`. Production selection fails closed.
- `codex exec` receives the combined system/user prompt through stdin and the
  existing effective JSON Schema through a unique temporary `--output-schema`
  file. The final message is read from a unique `--output-last-message` file.
- Each invocation uses a unique read-only temporary working directory outside
  the repository. Prompt text is passed with `ProcessStartInfo.ArgumentList`
  and is never shell-interpolated. Timeout, cancellation, nonzero exit,
  malformed JSON, missing executable, and usage/authentication-like failures
  remain provider failures rather than scientific insufficiency.
- Normal tests and CI do not install, authenticate, or invoke Codex. Manual
  smoke tests are gated by `MEDRESEARCH_RUN_LIVE_CODEX_CLI=true`; the existing
  live E2E harness accepts `MEDRESEARCH_LLM_PROVIDER=CodexCli`.
- F11 deterministic tests pass, and the opt-in adapter/planner live smokes pass.
  A real PostgreSQL-backed run was exercised through Planning, Searching,
  Extracting, Evaluating, and Synthesizing, but ended in existing synthesis
  validation after Codex emitted an unsupported mixed/conflict claim; no live
  report completion is claimed. Startup migration registration now precedes
  the worker to avoid polling an unmigrated fresh database.

- Initial repository documentation and development trail.
- Architecture comprehension documentation:
  - `docs/architecture-overview.md` for a simple Russian system overview.
  - `docs/request-lifecycle.md` for the code-level request/pipeline trace.
  - `docs/learning-path.md` for a staged reading and tracing guide.
- .NET 10 solution file: `MedResearch.slnx`.
- Local .NET tool manifest with `dotnet-ef`.
- Layered projects:
  - `src/MedResearch.Api`
  - `src/MedResearch.Application`
  - `src/MedResearch.Domain`
  - `src/MedResearch.Infrastructure`
- Test projects:
  - `tests/MedResearch.Domain.Tests`
  - `tests/MedResearch.Application.Tests`
  - `tests/MedResearch.Infrastructure.Tests`
  - `tests/MedResearch.IntegrationTests`
- Standard ASP.NET Core health check endpoints at `/health`, `/health/live`, and `/health/ready`.
- Infrastructure registration through `services.AddInfrastructure(configuration)`.
- Application registration through `services.AddApplication()`.
- Frontend workspace under `frontend/`:
  - Next.js web app for dashboard, research creation, research run history, run detail, and report display.
  - Tauri desktop shell foundation.
  - shared `@medresearch/api` package with generated OpenAPI contracts, typed fetch client, Zod response validation, query keys, and status helpers.
  - shared `@medresearch/ui` primitives.
  - frontend tests use mocked backend responses and do not call live scientific or AI providers.
- First end-to-end research API use case:
  - `POST /api/research` creates a `ResearchQuestion` and queued `ResearchRun`.
  - `GET /api/research` retrieves paginated research run history with optional exact status filtering.
  - `GET /api/research/{researchRunId}` retrieves the run state and original question.
  - `GET /api/research/{researchRunId}/progress` retrieves persisted execution progress, processing lease state, stage states, and run-scoped counters without invented percentages or ETA.
  - `GET /api/research/{researchRunId}/report` retrieves the persisted synthesis report when ready.
  - API endpoints call Application use cases and do not query EF directly.
  - Invalid input, missing runs, and unexpected failures are returned as Problem Details.
- Durable lease-backed background research processing:
  - `BackgroundResearchWorker` runs as an ASP.NET Core hosted service in `MedResearch.Api` and uses an operational worker instance id.
  - `ResearchRunProcessor` advances claimed runs through Planning, Searching, Extracting, Evaluating, Synthesizing, and Completed while renewing processing leases.
  - `Planning` calls the structured Research Planner and persists a validated `ResearchPlan`.
  - `Searching` consumes persisted `ResearchPlan.SearchQueries` and performs real PubMed plus Europe PMC retrieval through provider-neutral Application contracts and a multi-source coordinator.
  - `Extracting` acquires bounded SourceMaterial and performs source-grounded evidence extraction with explicit abstract/full-text scope for discovered studies.
  - `Evaluating` performs structured source-aware methodological evidence evaluation from study metadata, extraction provenance, and grounded evidence.
  - `Synthesizing` builds bounded current-run synthesis context and persists a traceable `ResearchReport` with claims linked to Evidence.
  - Runs move to Completed only after report persistence succeeds or an explicit insufficient-evidence report is created.
  - Expired in-progress leases in Planning, Searching, Extracting, Evaluating, or Synthesizing can be reclaimed at the current stage.
  - Progress/failure/heartbeat writes require lease owner and monotonically increasing lease version to prevent stale-worker overwrites.
  - Terminal states clear active lease metadata.
- Structured AI research planning:
  - `IStructuredLlmClient` provider-neutral Application boundary.
  - `ResearchPlanner` Application service.
  - `ResearchPlannerPrompt` with prompt version `research-planner-v1`.
  - OpenAI Infrastructure adapter using the Responses API with strict JSON Schema structured output.
  - Normal tests use fake LLM providers or fake HTTP and do not call the live OpenAI API.
- Scientific literature retrieval:
  - `IScientificLiteratureSource`
  - `IScientificLiteratureSearchCoordinator`
  - `IScientificSearchResultStore`
  - PubMed implementation using official NCBI E-utilities `esearch.fcgi` and `efetch.fcgi`
  - Europe PMC implementation using the official Articles REST `/search` endpoint with JSON `resultType=core`
  - PubMed `tool`/`email` identification support and optional `api_key`
  - PubMed result limit default: 10, fetch batch size default: 25, max request rate default: 2 requests/second
  - source-specific central local token-bucket rate limiting across PubMed ESearch/EFetch and Europe PMC REST pages
  - bounded retry for transient 429, 5xx, network, and timeout failures
  - batched EFetch retrieval instead of one request per PMID
  - one planned query executes once per enabled source, preserving separate `LiteratureSearch` rows per source
  - Europe PMC bounded cursor pagination through `cursorMark`/`nextCursorMark`
  - deterministic identifier normalization for PMID, PMCID, and DOI
  - hard stable-identifier conflicts are skipped/logged rather than silently merged
  - concurrent Study identity upserts serialize through PostgreSQL transaction-scoped advisory locks plus filtered unique indexes
  - multiple planned queries execute sequentially
  - zero-result searches are persisted and do not fabricate studies or evidence
- Source-grounded evidence extraction:
  - `IEvidenceExtractor` and `EvidenceExtractor` in Application.
  - `IEvidenceExtractionStore` implemented by `EfEvidenceExtractionStore` in Infrastructure.
  - Prompt version `evidence-extractor-v3-bound-tuples` with strict structured output, exact `pValueOperator` and nullable reported `timepoint`.
  - LLM input scope is limited to the current question, bounded plan context, and one study title/abstract/metadata item.
  - Studies with no usable abstract are recorded as skipped with `NoExtractableText` and are not sent to the LLM.
  - Supporting excerpts are resolved uniquely against the exact selected SourceMaterial using versioned `source-text-v1` normalization, canonical offsets, normalized span text, and a SHA-256 span hash.
  - Numeric grounding verifies local statistical association rather than token presence alone: effect measure/estimate, CI bounds, p-value/operator, standard error, and conservatively scoped sample size are persisted with `Verified`, `Ambiguous`, or `Unsupported` facts.
  - Quantitative synthesis rejects persisted numeric inputs without the required `Verified` grounding facts. Legacy persisted rows with empty grounding metadata are not silently treated as verified.
  - Provider, malformed output, validation, and grounding failures use the existing safe run failure path.
  - `EvidenceExtraction:MaxStudiesPerRun` defaults to 10 and is bounded between 1 and 50.
- Structured evidence evaluation:
  - `IEvidenceEvaluator` and `EvidenceEvaluator` in Application.
  - `IEvidenceEvaluationStore` implemented by `EfEvidenceEvaluationStore` in Infrastructure.
  - Prompt version `evidence-evaluator-v2-no-raw-summary` with strict structured output.
  - Creates one study-level `EvidenceEvaluation` per research run, study, and evaluator prompt version.
  - Stores evaluated evidence ids, source scope, provider/model/prompt provenance, categorical methodological domains, deterministic signal booleans, reporting limitations, author-reported limitations, and bounded overall methodological confidence.
  - Uses `Unknown`, `InsufficientSource`, and `NotApplicable` to distinguish missing validated information, inadequate current source scope, and conceptually irrelevant domains.
  - Does not assign numeric quality scores and does not claim formal GRADE, RoB 2, ROBINS-I, AMSTAR-2, NOS, or other validated framework output.
  - Studies with no extracted evidence are recorded as skipped with `NoExtractedEvidence` and are not sent to the LLM.
  - Provider, malformed output, validation, and unsupported methodological claims use the existing safe run failure path.
  - `EvidenceEvaluation:MaxStudiesPerRun` defaults to 10 and is bounded between 1 and 50.
- Traceable evidence synthesis:
  - `ISynthesisCorpusStore`, `ISynthesisContextBuilder`, `IResearchSynthesizer`, and `IResearchReportStore` in Application.
  - `SynthesisContextBuilder` validates current-run corpus identity, discovered-study membership, evidence/evaluation/extraction/search run scope, and evaluation EvidenceIds.
  - Context selection is deterministic and bounded by `Synthesis:MaxStudies`, `Synthesis:MaxEvidenceFindings`, and `Synthesis:MaxClaims`.
  - Prompt version `research-synthesizer-v2-trusted-evidence` with strict structured output and no raw extraction ResultSummary.
  - Every persisted completed-report claim must cite supplied EvidenceIds from the same ResearchRun.
  - Citation authority comes from persisted Evidence and Study rows; model-supplied PMID, DOI, and StudyId are rejected.
  - No validated evidence produces a deterministic `InsufficientEvidence` report without an LLM call.
  - Persisted ResearchReport synthesis remains narrative and traceable; deterministic common/fixed-effect, REML random-effects Wald pooled ratio results, canonical HKSJ summary-effect inference, and random-effects prediction intervals may be supplied as bounded context, but no automatic model selection, modified/ad-hoc HKSJ, vote counting, formal GRADE, formal RoB, diagnosis, or treatment recommendation is produced.
- Application persistence boundaries:
  - `IResearchStore` for HTTP create/read use cases.
  - `IResearchProgressStore` for persisted execution-progress read models.
  - `IResearchRunQueue` for worker claim/progress/failure operations.
  - `IResearchPlanStore` for accepted ResearchPlan persistence and lookup.
  - `IScientificSearchResultStore` for normalized scientific candidates, search provenance, and discovery links.
  - `IEvidenceExtractionStore` for extraction work items, provenance, idempotency, and evidence persistence.
  - `IEvidenceEvaluationStore` for evaluation work items, provenance, idempotency, and structured assessment persistence.
  - `ISynthesisCorpusStore` for loading current-run synthesis corpus snapshots.
  - `IResearchReportStore` for idempotent report persistence and report read models.
- EF Core PostgreSQL persistence in Infrastructure:
  - `MedResearchDbContext`
  - explicit entity configurations for ResearchQuestion, ResearchRun, ResearchPlan, Study, Evidence, EvidenceExtraction, EvidenceEvaluation, LiteratureSearch, ResearchStudyDiscovery, ResearchReport, ResearchReportClaim, and ResearchReportClaimEvidence
  - migrations:
    - `20260830063109_InitialCreate`
    - `20260830114130_AddLiteratureSearchProvenance`
    - `20260830160612_AddStructuredResearchPlans`
    - `20260831142411_AddSourceGroundedEvidenceExtraction`
    - `20260831171340_AddStructuredEvidenceEvaluations`
    - `20260901021148_AddTraceableResearchReports`
    - `20260901063528_AddResearchRunProcessingLeases`
    - `20260902031207_AllowMultipleDiscoveryPathsPerStudy`
    - `20260902150845_AddStudyPmcidIdentity`
    - 20260908074149_AddSourceMaterials
    - `20260928164923_AddResearchRunHistoryIndex`
- Docker Compose local development environment:
  - `postgres` service using PostgreSQL 17 Alpine
  - `api` service for `MedResearch.Api`, including the hosted background worker
  - development-only defaults in `.env.example`
  - OpenAI, PubMed, and Europe PMC environment placeholders in `.env.example`, including source rate/batch/page/retry knobs
  - `EvidenceExtraction__MaxStudiesPerRun` wired from `.env.example`
  - `EvidenceEvaluation__MaxStudiesPerRun` wired from `.env.example`
  - `Synthesis__MaxStudies`, `Synthesis__MaxEvidenceFindings`, and `Synthesis__MaxClaims` wired from `.env.example`
  - `ResearchProcessing__LeaseDurationSeconds` and `ResearchProcessing__HeartbeatIntervalSeconds` wired from `.env.example`
- Domain concepts:
  - `ResearchQuestion`
  - `ResearchRun`
  - `ResearchRunStatus`
  - `ResearchPlan`
  - `Study`
  - `LiteratureSearch`
  - `ResearchStudyDiscovery`
  - `EvidenceExtraction`
  - `EvidenceExtractionStatus`
  - `EvidenceExtractionSkipReason`
  - `Evidence`
  - `EvidenceDirection`
  - `EvidenceSourceScope`
  - `EvidenceEvaluation`
  - `EvidenceEvaluationStatus`
  - `EvidenceEvaluationSkipReason`
  - `StudyDesignClassification`
  - `MethodologicalAssessmentState`
  - `ComparatorPresence`
  - `DirectnessRating`
  - `MethodologicalConfidence`
  - `ResearchReport`
  - `ResearchReportStatus`
  - `ResearchReportInsufficientEvidenceReason`
  - `ResearchReportClaim`
  - `ResearchReportClaimType`
  - `ResearchReportClaimDirection`
  - `ResearchReportClaimEvidence`
  - `SynthesisConfidence`
- Tests:
  - Domain unit tests for question validation, research run lifecycle behavior, and representative invalid transition matrix checks.
  - Application tests for queued run creation, retrieval miss, progress stage/lease read-model semantics, processing orchestration, planner validation, original-question preservation, planning failure, search behavior, evidence extraction validation, grounding, numeric grounding, skips, deduplication, evidence evaluation validation, source-awareness, no-score enforcement, synthesis context construction, synthesis validation, conflict preservation, insufficient-evidence reports, cancellation, provider failure propagation, and source-level architecture boundaries for Domain/Application.
  - Infrastructure tests for OpenAI Responses API request/response mapping and PubMed parsing/fake HTTP behavior, including request parameters, optional API key handling, batching, retry, cancellation, XML edge cases, and DOI/PMID normalization; Europe PMC fake HTTP behavior, including request parameters, cursor pagination, retry, cancellation, malformed JSON, mapping, deduplication, and PMID/PMCID/DOI normalization.
  - API integration tests using `WebApplicationFactory` and fake stores, so endpoint behavior runs without Docker and does not start hosted services.
  - PostgreSQL integration tests using Testcontainers for research persistence, multiple ResearchRuns per ResearchQuestion, queue semantics, lease recovery, heartbeat, stale-owner fencing, plan/search persistence, multi-search discovery provenance, conservative Study identity edge cases, PMCID identity, hard multi-identifier conflicts, multi-source discovery provenance, extraction deduplication after repeated discovery, persisted progress counters, evidence extraction persistence, evidence evaluation persistence, report persistence, report relationships, idempotency, authoritative citation reconstruction, shared-Study/run-scoped Evidence citation graphs, fresh migration application, current-run corpus loading, and a full fake-provider vertical pipeline. They run against real PostgreSQL when Docker is reachable and are currently skipped locally because the Docker Desktop engine is unavailable. They do not fall back to EF Core InMemory.
  - GitHub Actions CI requires Docker-backed Testcontainers tests with `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true` and fails on unexpected skipped tests in required-Docker mode.

## Environment Status

- GitHub remote is configured as `https://github.com/DanDan919/MedResearch.git`.
- Docker CLI and Docker Compose are installed, but Docker Desktop engine is not reachable at `//./pipe/dockerDesktopLinuxEngine` in this environment.
- Local Docker Compose health-check runtime verification has not passed because the Docker Compose stack cannot start while the engine is unavailable. `/health/ready` is covered by the PostgreSQL-backed fake-provider integration test when Docker is available.
- Optional live PubMed and Europe PMC smoke test projects exist outside `MedResearch.slnx`; they are not run by default or by normal CI. Normal tests use fixtures and fake HTTP.
- No live OpenAI smoke test is configured or run by default. Normal tests use fake LLM providers and fake HTTP.

### F14 release-candidate audit

- F14 performed a code-first adversarial audit of ownership, pipeline lifecycle,
  worker recovery/fencing, LLM validation, scientific provenance, quantitative
  artifacts, frontend/API contracts, and CI coverage.
- The audit is recorded in `docs/audits/f14-full-system-release-candidate-audit-ru.md`.
- A real API defect was fixed: the allow-listed CORS middleware now runs before
  authentication/authorization, so browser preflight requests for protected
  research routes are answered before the endpoint authorization challenge.
- The fix has an API integration regression test. It does not weaken endpoint
  authorization: the actual research request remains protected.
- The audit does not claim that numeric substring grounding proves semantic
  statistic-to-field association, that provider failures have persisted
  first-class attempt rows, or that report/artifact linkage is a persisted FK.
- Post-F14 GitHub Actions run `37279009249` completed successfully on the audit
  commit; both Ubuntu jobs passed, including strict Docker/Testcontainers,
  frontend Playwright, EF model, and Compose checks.

## Next Logical Milestone

Keep hardening trust boundaries, retry behavior, provider diagnostics, and identity-conflict observability before adding a third scientific source or another LLM provider. Do not add diagnosis, treatment recommendations, or patient-specific medical advice.

## Not Yet Implemented

- Crossref, OpenAlex, Semantic Scholar, publisher API, or additional non-PubMed/non-Europe-PMC source integrations.
- Additional LLM providers beyond OpenAI.
- Full-text extraction.
- Formal study quality frameworks such as GRADE, RoB 2, ROBINS-I, AMSTAR-2, or NOS.
- Full-text evidence synthesis.
- Modified/ad-hoc HKSJ, tau-squared confidence intervals, forest plots, p-value pooling, and broad pooled-effect families beyond inverse-variance OR/RR/HR V1. Canonical HKSJ summary-effect inference and random-effects prediction intervals now exist for successful REML random-effects OR/RR/HR groups. Q/df/I-squared diagnostics and REML random-effects Wald synthesis exist for successful compatible groups.
- Semantic outcome harmonization.
- Cohort-overlap or citation-overlap detection for systematic reviews and primary studies.
- RAG/vector search.
- Distributed PubMed/Europe PMC rate limiting across multiple API instances.
- PubMed History Server retrieval for larger result windows.
- OpenAI retry policy.
- Production migration strategy.

## Milestone 12 Notes

- Actual starting HEAD was `b0e5e7c` on `main`, not the older pre-PubMed-hardening reference.
- Official Europe PMC REST documentation was checked before implementation. The adapter uses production base `https://www.ebi.ac.uk/europepmc/webservices/rest/`, `/search`, `format=json`, `resultType=core`, `pageSize`, and `cursorMark` pagination.
- `Study` now includes nullable normalized `Pmcid` with a filtered unique PostgreSQL index.
- `ScientificLiteratureSearchCoordinator` is the single Application orchestration boundary for enabled sources. It records each source/query execution independently and only fails a query when every enabled source fails.
- Normal CI and normal local tests remain deterministic and make no live PubMed or Europe PMC requests.

## Source Material and Evidence Corpus

The source-material layer is now persisted and used as the authoritative extraction input:

- SourceMaterial stores exact abstract or bounded Europe PMC structured full-text snapshots with provider/retrieval provenance, SHA-256 content hash, version, current flag, access status, section names, and truncation metadata.
- Same-content ingestion reuses a snapshot; changed content creates a new version and never mutates historical content referenced by an extraction.
- EvidenceExtraction.SourceMaterialId is required for completed extraction and is included in extraction idempotency.
- EvidenceCorpusBuilder validates a run-scoped EvidenceCorpus before synthesis. It rejects cross-run Evidence, mismatched Study/source lineage, ungrounded completed extractions, duplicate Study snapshots, and evaluation references outside the corpus.
- The corpus computes descriptive source-coverage metrics and conservative normalized outcome conflicts. These metrics are not quality weights and are not statistical synthesis.
- Europe PMC full text uses the official fullTextXML endpoint only. Unavailable full text falls back to an abstract; provider failure is logged distinctly; neither condition invents Evidence or automatically fails a run.
- Normal CI remains external-service independent. Live Europe PMC full-text and live provider smoke tests remain explicit opt-in projects outside the solution.

## Quantitative Evidence Eligibility

- Added `QuantitativeEvidenceAssessor` in Application as a deterministic read-model builder over validated EvidenceCorpus.
- Added explicit effect-measure classification and eligibility reason codes for future quantitative synthesis input checks.
- Added source-reported `ConfidenceLevel` and `ReportedStandardError` to Evidence persistence through migration `20260916032923_AddEvidenceQuantitativeStatistics`.
- Quantitative readiness can derive log ratio effects, CI/SE-based variance, and Fisher z correlations in C# only; the LLM is not used as a calculator.
- CompatibleEvidenceGroup is a readiness grouping, not a pooled result. It tracks unique Study count and refuses to treat multiple Evidence from one Study as independent.
- Narrative ResearchReport persistence remains unchanged; deterministic fixed-effect pooled ratio results are supplied as synthesis context, not as a persisted report table or broad meta-analysis claim.

## Milestone 16 Live Validation Harness

- Added `ResearchPlanning:MaxSearchQueries` as a normal configuration setting. The default remains 5, matching the existing planner maximum; live validation can reduce it to 2 without changing production code paths.
- Added `tests/MedResearch.LiveE2EValidationTests` outside `MedResearch.slnx`. It is skipped unless `MEDRESEARCH_RUN_LIVE_E2E=true` and required live configuration is present.
- The live E2E harness uses `WebApplicationFactory<Program>` and production DI/hosted services. It verifies `/health/ready`, submits `POST /api/research`, waits for the worker to complete the ResearchRun, and reads the report endpoint.
- Normal CI and normal local solution tests remain deterministic and do not call live OpenAI, PubMed, Europe PMC, or full-text endpoints.

## F12 Validation-Guided LLM Repair

- Added provider-neutral `ValidationGuidedLlmRepairService` in Application.
- `ValidationIssue` carries stable code, optional contract path, bounded repair instruction, and repairable/non-repairable disposition.
- Evidence extraction and synthesis may make one bounded complete-replacement attempt after typed repairable validation failure. The same trusted SourceMaterial/SynthesisContext, schema, and validator are reused; rejected output is never persisted.
- Cross-run context, provider/transport failure, cancellation, and infrastructure invariants fail closed without semantic repair. Planner and evaluator semantic repair remain intentionally disabled.
- Deterministic Application tests cover successful and failed repair, no repair for non-repairable/provider failures, exact context/schema reuse, extraction grounding, synthesis direction, and cross-run precondition.
- Live F12 classification remains pending the explicit Codex CLI + PubMed + Europe PMC + isolated PostgreSQL run. Normal CI remains external-service independent.

## Fixed-Effect Quantitative Synthesis V1

- Added `FixedEffectQuantitativeStatisticalSynthesizer` in Application as a deterministic read model over M15 `CompatibleEvidenceGroup` output.
- V1 supports only OR/RR/HR compatible groups with independent Study contributions, finite normalized log effects, and finite positive variance.
- The model computes generic inverse-variance fixed-effect weights, pooled log effect, variance, standard error, configured two-sided confidence interval, and exponentiated reported-scale result.
- Output is exposed through `SynthesisContext.QuantitativeSyntheses` and, since F6, persisted as an immutable quantitative artifact with exact contribution snapshots.
- The narrative synthesis prompt may receive deterministic pooled results but is forbidden from calculating or altering pooled estimates itself.
- Defaults: `QuantitativeSynthesis:OutputConfidenceLevel=0.95`, `QuantitativeSynthesis:MinimumUniqueStudies=2`.
- Still not implemented: modified/ad-hoc HKSJ, tau-squared confidence intervals, forest plots, MD/SMD/correlation pooling, p-value pooling, semantic outcome harmonization, or cohort-overlap correction.
## Fixed-Effect Heterogeneity Diagnostics V1

Milestone 18 extends the transient quantitative synthesis read model with deterministic heterogeneity diagnostics for successful M17 fixed-effect groups. `HeterogeneityDiagnosticsCalculator` computes Cochran's Q, degrees of freedom, and I-squared using the same M17 contribution set, analysis-scale effects, pooled analysis-scale effect, and inverse-variance weights.

The diagnostics are versioned as `cochran-q-i2-v1`, projected into `SynthesisContext` and the synthesis prompt, and included in the F6 quantitative artifact snapshot. They are not persisted as Evidence or Study metadata and do not have a separate diagnostics table.

Scope intentionally not implemented: tau-squared confidence intervals, Q p-values, forest plots, funnel plots, publication-bias tests, subgroup analysis, automatic model selection, and modified/ad-hoc HKSJ.
## REML Between-Study Variance Foundation V1

Milestone 19 extends successful transient quantitative synthesis results with `BetweenStudyVarianceEstimate` using `RestrictedMaximumLikelihoodTauSquaredEstimator`.

- The estimator consumes the exact same independent M17 Study contributions, analysis-scale effects, and positive variances already accepted for fixed-effect synthesis.
- Tau-squared is estimated on the analysis scale; for OR/RR/HR this means squared log-ratio units.
- Boundary `tau² = 0`, failed bracketing, non-finite arithmetic, and max-iteration behavior are explicit rather than hidden behind a fabricated zero.
- The implementation is pure Application code with deterministic finite bracketing and bounded bisection.
- Values are projected into `SynthesisContext` and the synthesis prompt with algorithm version `reml-tau-squared-v1`.
- F6 adds a forward-only migration for persisted quantitative result artifacts; M17 fixed-effect pooled values and M18 Q/df/I-squared semantics remain unchanged.
- M17 fixed-effect pooled values and M18 Q/df/I-squared semantics remain unchanged.
- M22 implements REML random-effects weights and a Wald pooled random-effects estimate. M23 adds canonical HKSJ summary-effect inference beside Wald. M24 adds random-effects prediction intervals beside Wald/HKSJ. Still not implemented: modified/ad-hoc HKSJ, Q-profile tau-squared intervals, automatic model selection, and tau-based I-squared replacement.

## Random-Effects REML/Wald Quantitative Synthesis V1

Milestone 22 adds `RandomEffectsQuantitativeStatisticalSynthesizer` as a deterministic Application read model nested under each successful common/fixed-effect synthesis result.

- The random-effects result consumes the M19 `BetweenStudyVarianceEstimate`; it does not estimate tau-squared again.
- Raw random-effects weights are `1 / (vi + tau²)` using the same validated independent Study-level contribution population as M17-M19.
- The pooled random-effects estimate, variance, standard error, and confidence interval are computed on the analysis scale.
- OR/RR/HR reported-scale values are back-transformed with `exp(...)` only after analysis-scale synthesis is complete.
- The confidence interval is standard-normal Wald only for M22.
- `tau² = 0` is valid and collapses to the M17 common/fixed-effect inverse-variance result for the same contribution set.
- If M19 tau-squared is `NotEstimated`, the random-effects result is explicitly unavailable rather than fabricating zero or falling back to M17.
- Values are projected into `SynthesisContext` and the synthesis prompt. The LLM is forbidden from calculating or altering them.
- F6 persists the full deterministic result and fixed/random contribution snapshots; the read endpoint is machine-readable and no quantitative UI is included.
- Still not implemented: modified/ad-hoc HKSJ, tau-squared confidence intervals, model recommendation/selection, subgroup analysis, meta-regression, publication-bias methods, and forest plots.
## Milestone 21 Architecture Verification Audit

Milestone 21 independently audited the post-M20 architecture claims before adding any new scientific capability. The audit found that the documented major boundaries still match the implementation: Study remains global, search/discovery provenance remains source/query-specific, Evidence/Evaluation/Report data remains ResearchRun-scoped, synthesis rejects model-supplied citation identifiers, and worker lease owner/version fencing is enforced in PostgreSQL queue writes.

Changes from the audit were deliberately test-focused:

- enabled an existing PostgreSQL insufficient-evidence report persistence test that lacked an xUnit attribute;
- added stale-owner negative tests for lease renewal, failure marking, and release;
- added EvidenceCorpusBuilder negative tests for broken extraction lineage and cross-run search provenance;
- recorded the verification matrix in `docs/development/milestone-21-verification-ru.md`.

No production behavior, schema, provider, quantitative semantics, or Docker configuration changed in this milestone.

## F9 Research Integrity and Recovery Hardening

F9 makes two recovery windows idempotent without changing the scientific pipeline: a persisted ResearchPlan is reused for a matching ResearchRun/question/prompt contract, and a successful literature search execution is reused by `(ResearchRunId, ResearchPlanId, Source, Query)`. The search execution key is protected by a forward-only PostgreSQL unique index. Conflicting plan inputs are rejected rather than silently replacing the accepted plan.

F9 also corrects empty-evidence coverage semantics: no validated Evidence is not reported as abstract-only evidence. The frontend CI workflow now installs Chromium and runs the existing deterministic Playwright suite. Local Docker-backed integration tests may still skip when Docker Desktop is unavailable; CI remains authoritative for PostgreSQL execution.

Historical F9 limitation: provider failures originally had only logs. F15.2 closes that gap through separate durable LiteratureProviderAttempt history while keeping LiteratureSearch as successful scientific provenance. Acquisition-failure history remains separate debt.

## Frontend F3 Research Execution Observatory

F3 adds a backend-backed progress read model and upgrades `/research/[id]` from a simple status page into a live execution observatory.

- Backend endpoint: `GET /api/research/{researchRunId}/progress`.
- Application boundary: `GetResearchProgressUseCase` + `IResearchProgressStore`.
- Infrastructure implementation: `EfResearchProgressStore`, which projects only persisted facts from PostgreSQL.
- Frontend uses the generated OpenAPI contract, `MedResearchApiClient.getResearchProgress`, TanStack Query polling while non-terminal, and Zod response validation.
- The page shows pipeline stage states, persisted counters, run timestamps, processing lease state, and safe failure information.
- It deliberately does not show percentages, ETA, live provider activity, local scientific calculations, or a guessed failed stage.
- No database schema change was added. Failure stage remains not persisted; failure is displayed as terminal run state with safe failure reason.
- SourceMaterial is global per Study, so progress counts current source material available for the Studies discovered by the run.

## F13 Evidence & Provenance Explorer

F13 adds the owner-authorized `GET /api/research/{researchRunId}/provenance` endpoint and `/research/[id]/evidence` read-only workspace. The backend projects one ResearchRun's persisted search executions, per-search discovery paths, canonical Studies, SourceMaterial metadata, run-scoped extraction/Evidence/Evaluation records, report claim Evidence links, and quantitative contribution lineage. A study discovered by PubMed and Europe PMC appears once with multiple discovery paths.

The endpoint intentionally excludes raw `SourceMaterial.Content`. The UI shows content hashes, versions, access status, character counts, and sections instead. It does not invent identifiers, citations, confidence, or study-level quantitative intervals. Search results with zero records are successful zero-result executions. Since F15.2, separate LiteratureProviderAttempt history also exposes failed/timeout/cancelled/unfinished calls; historical absence of attempt rows is not reconstructed.

Cross-run isolation is enforced in the EF projection by owner-filtering the ResearchRun and filtering all run-scoped child records by the requested run. Report claim Evidence links are additionally joined to same-run Evidence. PostgreSQL integration coverage verifies the global Study/multiple discovery relationship and run-scoped Evidence graph; API and Playwright tests cover authorization, zero evidence, source-content exclusion, claim links, and quantitative lineage.
## Canonical HKSJ Summary-Effect Inference V1

Milestone 23 adds `HksjSummaryEffectInferenceCalculator` as a deterministic Application read model nested under each successful M22 random-effects result.

- HKSJ reuses the M22 random-effects point estimate, weights, contribution population, Wald variance, and configured confidence level.
- Degrees of freedom are `k - 1`; `k = 1` is explicitly unavailable with `df = 0`.
- The HKSJ interval uses Student-t critical values and the canonical variance adjustment `sum(w_i_RE * (theta_i - theta_RE)^2) / (k - 1)`.
- Canonical HKSJ is exposed beside, not instead of, the existing Wald result.
- The synthesis prompt receives deterministic HKSJ values and forbids the LLM from calculating or altering them.
- Before F6 these values were transient; F6 now persists the complete deterministic result as a run-scoped artifact with exact contribution snapshots.
- Still not implemented: modified/ad-hoc HKSJ, tau-squared confidence intervals, p-values, automatic inference/model selection, forest plots, and quantitative UI.

## Random-Effects Prediction Interval V1

Milestone 24 adds `RandomEffectsPredictionIntervalCalculator` as a deterministic Application read model nested under each successful M22 random-effects result.

- The prediction interval reuses the M22 random-effects point estimate, Wald summary variance, contribution population, configured confidence level, and the M19 REML tau-squared estimate.
- It uses Student-t critical values with `df = k - 1`.
- The analysis-scale prediction variance is `Var(theta_RE) + tau²`.
- OR/RR/HR reported-scale prediction interval endpoints are produced by exponentiating the analysis-scale endpoints after all arithmetic is complete.
- `k = 1` returns an explicit unavailable prediction interval with `df = 0`.
- `tau² = 0` is valid; the prediction variance then collapses to the M22 Wald summary variance and still uses the prediction-interval Student-t critical value.
- Prediction intervals are exposed beside Wald and HKSJ rather than replacing or selecting among them.
- The synthesis prompt receives deterministic prediction interval values and forbids the LLM from calculating or altering them.
- Still not implemented: prediction interval model selection/recommendation, modified/ad-hoc HKSJ, tau-squared confidence intervals, prediction intervals for unsupported effect families, p-values, forest plots, and quantitative UI.

## Frontend F4 Scientific Report Workspace

F4 turns `/research/[id]/report` into a traceable report workspace backed by the existing persisted report endpoint.

- The API read model projects ordered report claims, claim-to-Evidence links, authoritative Study identifiers and metadata, and SourceMaterial lineage metadata without returning raw source content.
- The UI renders the persisted narrative and coverage facts, keeps claim order and citation order, and uses native expandable sections for evidence details.
- PMID, PMCID, and DOI links are rendered only when the API returns the corresponding identifier. Missing study metadata remains explicitly unavailable; no identifier or confidence score is invented in the browser.
- `404` (unknown run) and `409` (known run whose report is not ready) remain distinct UI states. Report print controls are hidden from printed output.
- Frontend unit and Playwright tests use mocked API responses and do not call scientific or AI providers.

## F6 Persisted Quantitative Synthesis Artifacts

F6 makes M17-M24 quantitative results durable without changing their formulas. `SynthesisContextBuilder` persists every deterministic group result, including non-estimated states, before the narrative synthesis call. The same in-memory result is still projected into `SynthesisContext`; the LLM does not calculate or select statistical values.

- `quantitative_synthesis_artifacts` stores the complete result snapshot, algorithm versions, statuses, failure reasons, counts, confidence level, and deterministic fingerprint.
- `quantitative_synthesis_contribution_snapshots` stores fixed-effect and random-effects contribution populations with exact `double` effect/variance/SE/weight values and FK lineage to Evidence, Study, EvidenceExtraction, and SourceMaterial.
- Artifact persistence is transactional and idempotent on `(ResearchRunId, GroupKey)` plus fingerprint. A different retry result cannot overwrite an existing artifact.
- `GET /api/research/{researchRunId}/quantitative` returns the machine-readable artifact read model without raw source text, prompts, credentials, or processing lease data.
- No quantitative frontend workspace was added; the generated API contract is ready for a later client milestone.

## F7 Quantitative Results Workspace

F7 adds `/research/{id}/quantitative` as a frontend read-only workspace over the persisted F6 artifact endpoint. It supports multiple returned groups, summary Common/Fixed and Random Effects, Q/df/I²/tau², Wald versus HKSJ inference, prediction intervals, a presentation-only SVG contribution plot, exact persisted contribution values, lineage IDs, artifact fingerprint, and algorithm metadata.

The frontend does not calculate any scientific quantity. It does not derive contribution confidence intervals from SE, calculate weights, exponentiate analysis-scale values, classify heterogeneity, choose a model, or make clinical recommendations. Study-level titles, identifiers, authors, and contribution-level CIs are not present in the F6 snapshot and remain explicitly unavailable. Report navigation exposes the quantitative link only when the run-scoped endpoint returns a non-empty artifact list.

## F12 Validation-Guided LLM Repair and First Completed Live Run

F12 adds a provider-neutral, typed, bounded semantic repair step for extraction and synthesis validation failures. A repair is attempted only for explicitly repairable issue codes, uses the same task context and schema, requests a complete replacement, and validates the replacement from scratch. Non-repairable failures, provider failures, cancellation, and exhausted budgets fail closed; rejected candidates are never persisted. The default semantic repair budget is one attempt, configurable from zero through two. Planner and evaluator semantic repair remain intentionally disabled.

Deterministic Application tests cover first-valid output, successful and failed replacement, non-repairable/provider/cancellation behavior, same-context/schema use, extraction grounding repair, synthesis direction repair, cross-run rejection, and bounded attempts. A fresh isolated PostgreSQL live run completed through Codex CLI, PubMed, Europe PMC, extraction, evaluation, synthesis, report persistence, and report retrieval. Its report was honestly `InsufficientEvidence` with no validated Evidence or claims; this proves runtime completion and persistence, not scientific completeness. Normal CI remains provider-independent and does not run live E2E.
