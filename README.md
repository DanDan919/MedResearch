# MedResearch

MedResearch is a portfolio and learning project for AI-assisted scientific evidence synthesis, focused primarily on medical and neuroscience research.

The purpose is not to diagnose patients or recommend treatments. The long-term goal is to help transform scientific questions into structured research workflows, retrieve study metadata, extract structured evidence, evaluate study quality where deterministic rules are possible, detect conflicting evidence, and produce traceable evidence syntheses.

## Current Scope

This repository currently contains the documentation system, layered .NET solution, PostgreSQL persistence through EF Core, a Docker Compose development environment, the first research API use case, durable lease-backed background processing for queued and recoverable research runs, a truthful persisted progress endpoint for research execution observability, structured AI research planning through OpenAI, and scientific literature retrieval through PubMed/NCBI E-utilities and Europe PMC REST search, source-grounded abstract evidence extraction, structured source-aware evidence evaluation, and traceable persisted evidence synthesis reports.

A client can submit a research question, receive a queued research run id, and retrieve lifecycle progress. The background processor sends only the current submitted research question to the configured OpenAI provider during `Planning`, validates strict structured output into a persisted `ResearchPlan`, then uses accepted plan search queries during `Searching` to retrieve bounded metadata from enabled scientific literature sources. During `Extracting`, it sends only the current question, bounded plan context, and one selected SourceMaterial snapshot and study metadata to the configured OpenAI provider, validates strict structured output, and persists source-grounded evidence with explicit source scope. During `Evaluating`, it combines study metadata, extraction provenance, and grounded evidence into categorical methodological assessments. During `Synthesizing`, it builds a bounded current-run synthesis context and persists a traceable `ResearchReport`. It does not yet implement RAG, diagnosis, treatment recommendations, full-text synthesis, modified/ad-hoc HKSJ, forest plots, formal GRADE, or formal risk-of-bias frameworks. It now includes narrow deterministic common/fixed-effect, REML random-effects Wald, canonical HKSJ, and random-effects prediction interval quantitative synthesis read models for eligible compatible ratio-measure evidence.

## Stack Direction

- C# and .NET 10
- ASP.NET Core Web API
- ASP.NET Core hosted background service
- EF Core and PostgreSQL
- OpenAI Responses API for strict structured planning, evidence extraction, evidence evaluation, and evidence synthesis output
- PubMed retrieval through official NCBI E-utilities
- Europe PMC retrieval through the official Articles REST API
- Docker Compose for local development
- xUnit
- Testcontainers for PostgreSQL integration tests
- Structured logging through `ILogger`
- Separate worker executable later only if lifecycle or deployment needs justify it
- Additional LLM/scientific providers later behind provider-neutral abstractions
- pgvector later only if needed

## Repository Layout

```text
src/
    MedResearch.Api
    MedResearch.Application
    MedResearch.Domain
    MedResearch.Infrastructure

frontend/
    apps/
        web
        desktop
    packages/
        api
        ui

tests/
    MedResearch.Domain.Tests
    MedResearch.Application.Tests
    MedResearch.Infrastructure.Tests
    MedResearch.IntegrationTests

docs/
    architecture/
        decisions/
    development/
```

## Local Development

Copy `.env.example` to `.env` if you want to override local ports, development database values, OpenAI credentials, PubMed options, or Europe PMC options. The checked-in values are development-only defaults and placeholders, not production secrets.

```bash
docker compose up --build
```

The API listens on `http://localhost:8080` by default. The health check is available at:

```text
GET /health
GET /health/live
GET /health/ready
```

The frontend workspace lives under `frontend/`. The web app is a Next.js/React/TypeScript client and the desktop shell is Tauri 2 with React/Vite. Both use shared API and UI packages and call the ASP.NET Core API; they do not perform scientific or statistical calculations locally.

```bash
cd frontend
pnpm install
pnpm dev
```

For details, see `docs/frontend/architecture.md`, `docs/frontend/development.md`, and `docs/frontend/api-gaps.md`.

The Docker Compose API service sets `Database__ApplyMigrationsOnStartup=true`, so the committed EF migrations are applied when the local stack starts. The same API service hosts the background research worker.

### Authentication and ownership

Research data is owner-scoped at the API and PostgreSQL query boundaries. The
owner is the immutable authenticated `sub` claim; clients cannot submit an
owner ID. `ResearchQuestion` is the ownership root and all run-scoped report,
progress, and quantitative reads join through that root. Canonical `Study`
metadata remains global scientific identity and is not duplicated per user.

Production uses ASP.NET Core JWT Bearer validation. Set
`Authentication__Mode=JwtBearer`, `Authentication__Authority`, and
`Authentication__Audience`; issuer, audience, signature, and token lifetime
are validated by the framework. The API does not issue tokens or implement a
password store.

Local Docker development uses the explicit `DevelopmentLocal` mode with a
deterministic `local-development-user` subject. This mode is rejected outside
the `Development` environment and is not a production authentication bypass.
The test suite uses a test-only authentication handler and never calls an
external identity provider. Bearer transport is the intended client boundary;
there is no localStorage token persistence or cookie authentication in this
milestone, so cookie-specific CSRF protection is not invented here.

Anonymous endpoints are `/health/live` and `/health/ready`. Research creation,
history, progress, report, and quantitative endpoints require authentication.
An unauthorized or nonexistent research run has the same 404-style resource
behavior after authentication, avoiding intentional existence disclosure.

Existing rows from before ownership was introduced are migrated to the explicit
`legacy-unowned` subject and are not silently assigned to the first caller.
They require an explicit future migration/ownership tool before a user can
access them.

Background processing can be configured with `ResearchProcessing:Enabled`, `ResearchProcessing:IdleDelayMilliseconds`, `ResearchProcessing:LeaseDurationSeconds`, and `ResearchProcessing:HeartbeatIntervalSeconds`. The heartbeat interval must be positive and shorter than the lease duration. Evidence extraction volume can be configured with `EvidenceExtraction:MaxStudiesPerRun`; the default is 10 and the application bounds it between 1 and 50. Evidence evaluation volume can be configured with `EvidenceEvaluation:MaxStudiesPerRun` with the same default and bounds. Synthesis context size can be configured with `Synthesis:MaxStudies`, `Synthesis:MaxEvidenceFindings`, and `Synthesis:MaxClaims`; defaults are 10, 40, and 12. Quantitative synthesis can be configured with `QuantitativeSynthesis:OutputConfidenceLevel` and `QuantitativeSynthesis:MinimumUniqueStudies`, defaulting to 0.95 and 2. The same confidence level is used for common/fixed-effect, M22 random-effects Wald, M23 canonical HKSJ, and M24 random-effects prediction intervals.

AI planning can be configured with:

- `AI:Provider`, `OpenAI` or development/manual-only `CodexCli`
- `AI:BaseUrl`, default `https://api.openai.com/v1/`
- `AI:Model`, externally supplied
- `AI:ApiKey`, externally supplied secret
- `AI:TimeoutSeconds`, default 30
- `AI:MaxOutputTokens`, default 2000
- `AI:ValidationGuidedRepair:MaxSemanticRepairAttempts`, default `1`, bounded to `0-2`; this is a semantic repair budget, not transport retry

Codex CLI development provider

`AI:Provider=CodexCli` selects the provider-neutral `IStructuredLlmClient`
implementation backed by the installed `codex exec` process. It is accepted
only in `Development` or `ManualScientificE2E`; production startup rejects it.
The adapter uses the user's existing Codex/ChatGPT CLI authentication and
does not read tokens or require `OPENAI_API_KEY`. Codex CLI is a local process,
but model inference may be remote through the user's account, so it is not an
offline/local model.

Configure the optional development provider with `AI:CodexCli:ExecutablePath`
(`codex` by default), `AI:CodexCli:Model`,
`AI:CodexCli:TimeoutSeconds` (10-1800 seconds),
`AI:CodexCli:MaxPromptCharacters` (1000-2000000), and the fixed
`AI:CodexCli:Sandbox=read-only`. Each request uses stdin, a unique temporary
schema/output directory, `--output-schema`, and `--output-last-message`; the
MedResearch repository is not used as Codex's working directory. Prompt input
is never silently truncated. Normal tests and CI never invoke Codex. Opt-in
manual checks use `MEDRESEARCH_RUN_LIVE_CODEX_CLI=true` and the live E2E
harness uses `MEDRESEARCH_LLM_PROVIDER=CodexCli`.

PubMed can be configured with:

- `PubMed:BaseUrl`
- `PubMed:Enabled`, default `true`
- `PubMed:BaseUrl`, default `https://eutils.ncbi.nlm.nih.gov/entrez/eutils/`
- `PubMed:MaxResultsPerQuery`, development default `10`, bounded to 1-200
- `PubMed:FetchBatchSize`, development default `25`, bounded to 1-200
- `PubMed:MaxRequestsPerSecond`, development default `2`; cannot exceed 3 without `ApiKey` or 10 with `ApiKey`
- `PubMed:TimeoutSeconds`, development default `15`, bounded to 1-120
- `PubMed:Tool`, default `MedResearch`, sent on E-utilities requests
- `PubMed:Email`, optional contact, sent when configured
- `PubMed:ApiKey`, optional secret, sent as `api_key` only when configured
- `PubMed:MaxRetryAttempts`, development default `2`, bounded to 0-5
- `PubMed:RetryBaseDelayMilliseconds`, development default `250`

Europe PMC can be configured with:

- `EuropePmc:Enabled`, default `true`
- `EuropePmc:BaseUrl`, default `https://www.ebi.ac.uk/europepmc/webservices/rest/`
- `EuropePmc:MaxResultsPerQuery`, development default `10`, bounded to 1-200
- `EuropePmc:PageSize`, development default `25`, bounded to 1-100
- `EuropePmc:TimeoutSeconds`, development default `15`, bounded to 1-120
- `EuropePmc:MaxRequestsPerSecond`, development default `2`, bounded to 1-5 as a conservative local policy
- `EuropePmc:MaxRetryAttempts`, development default `2`, bounded to 0-5
- `EuropePmc:RetryBaseDelayMilliseconds`, development default `250`

Use `.env`, user secrets, or CI secrets for real OpenAI and NCBI API keys. Do not commit `.env`. The API can start and expose health endpoints without an OpenAI API key; a real processing run that reaches an OpenAI-backed stage fails through the normal safe failure path if required provider configuration is absent. Extraction and synthesis may perform one bounded validation-guided replacement after a repairable deterministic issue; the original candidate is never persisted and the replacement must pass the same validator from scratch.

## Research API

Create a queued research run from a question:

```text
POST /api/research
Content-Type: application/json

{
  "question": "Does chronic sleep deprivation impair working memory in adults?"
}
```

Successful responses return `201 Created`, a `Location` header, and the queued run id:

```json
{
  "researchRunId": "00000000-0000-0000-0000-000000000000",
  "status": "Queued"
}
```

Retrieve the current run state:

```text
GET /api/research/{researchRunId}
```

The lease-backed background worker may move the run through `Planning`, `Searching`, `Extracting`, `Evaluating`, `Synthesizing`, and `Completed`. If a worker disappears mid-run, another worker can reclaim an expired in-progress lease and retry from the persisted current stage. Matching persisted plans are reused during Planning recovery, and successful Searching executions are reused by the run/plan/source/query execution key. Stage stores also fence writes by the claimed worker id and lease version, so a stale worker cannot persist stage output after ownership transfer. Invalid questions, missing runs, not-ready reports, and server failures use ASP.NET Core Problem Details responses.

Retrieve persisted execution progress:

```text
GET /api/research/{researchRunId}/progress
```

The progress endpoint returns the run status, timestamps, safe failure reason, processing lease state, persisted stage states, and counters for plans, searches, discovery paths, distinct studies, source material, extraction, evidence, evaluation, reports, and claims. It does not return invented percentages, ETA, live provider activity, or guessed failed-stage labels. SourceMaterial is global per Study, so progress counts current source material available for Studies discovered by the run rather than claiming that every source snapshot was acquired uniquely by that run.

Retrieve the persisted synthesis report:

```text
GET /api/research/{researchRunId}/report
```

The report endpoint returns `200 OK` with coverage, deterministic limitations, claims, and authoritative Evidence/Study citations when a report exists. It returns `404 Not Found` for an unknown run and `409 Conflict` for a known run whose report is not ready.

## Research Planning

`Planning` uses a provider-neutral Application boundary for strict structured generation. Infrastructure currently implements that boundary with the OpenAI Responses API using JSON Schema structured output.

The planner output is treated as untrusted external input. It is deserialized, validated by Application, and only then persisted as `ResearchPlan`. The authoritative `ResearchQuestion` remains separate; the LLM-generated `originalQuestion` field must match the stored question after whitespace normalization and cannot overwrite it.

The prompt version is `research-planner-v1`. The planner is allowed to produce question decomposition and search strategy only. It must not produce PMIDs, DOIs, invented papers, authors, effect sizes, sample sizes, confidence intervals, p-values, evidence grades, diagnoses, treatments, or scientific conclusions.

## Scientific Retrieval

`Searching` is now multi-source. Application depends on provider-neutral literature contracts and a single `IScientificLiteratureSearchCoordinator`; Infrastructure supplies enabled `IScientificLiteratureSource` adapters for PubMed and Europe PMC.

```text
ResearchQuestion -> ResearchPlan -> SearchQueries -> source-specific searches -> normalized Study candidates -> Study identity resolution -> ResearchStudyDiscovery -> PostgreSQL
```

Each planned query is executed once per enabled source. One query against PubMed and Europe PMC therefore creates two `LiteratureSearch` provenance rows, not one merged search. A successful execution is idempotent for `(ResearchRunId, ResearchPlanId, Source, Query)`, so sequential recovery does not repeat an already persisted provider search. Each discovered publication creates a `ResearchStudyDiscovery` for that specific search execution. The same canonical `Study` can have multiple discovery paths in one run, while downstream extraction, evaluation, and synthesis deduplicate study work by `StudyId` within the run.

PubMed uses ESearch with `db=pubmed`, `retmode=json`, and bounded `retmax`, followed by batched EFetch XML. Requests include configured `tool`/`email` identification and optional `api_key`; ESearch and EFetch share one local token-bucket limiter. PubMed History Server retrieval remains deliberately deferred while `MaxResultsPerQuery` is small and direct ID batching is sufficient.

Europe PMC uses the official Articles REST `/search` endpoint with `format=json`, `resultType=core`, bounded `pageSize`, and cursor pagination through `cursorMark`/`nextCursorMark`. The adapter maps only source-reported PMID, PMCID, DOI, title, abstract, journal, publication date/date parts, publication types, authors, and provider record identity. It skips records without title or any stable identifier rather than merging by title.

Study identity is deterministic over normalized PMID, PMCID, and DOI. Missing metadata stays missing. Existing non-null metadata is not overwritten by null incoming values. New non-conflicting identifiers and metadata may enrich an existing Study. If an incoming candidate's stable identifiers point to different persisted Studies, MedResearch treats it as a hard identity conflict, logs bounded diagnostics, preserves existing Studies, skips the ambiguous discovery, and continues processing other candidates.

Both adapters use HttpClientFactory, cancellation tokens, process-local rate limiting, and bounded retries for transient provider failures. F15.2 adds durable `LiteratureProviderAttempt` outcomes: successful results, successful zero results, failed, timed out, cancelled, or started with no recorded outcome after interruption. Successful attempts are committed with `LiteratureSearch`/discovery output; failed attempts do not create scientific search results. Recovery reuses successful execution keys and records a new logical attempt for a failed/interrupted call. `/api/research/{id}/provenance` and the Evidence workspace expose this history under the existing owner boundary.

Success bodies have inclusive byte caps: PubMed ESearch 256,000, PubMed EFetch 2,000,000, Europe PMC search 2,000,000, and full-text XML 2,000,000 bytes. `PubMed:MaxSearchResponseBytes`, `PubMed:MaxFetchResponseBytes`, `EuropePmc:MaxResponseBytes` and both search providers' `BodyReadTimeoutSeconds` are env-overridable; the body timeout defaults to 15 seconds independently of the headers timeout. Full text uses its existing `TimeoutSeconds` for the body deadline. Oversized/malformed responses fail, rather than yielding partial Studies. Automatic HTTP URI logging is disabled for these clients, particularly because PubMed query strings may contain a key. Structured provider logs contain no raw error body or transport exception transcript.

Europe PMC publication metadata uses first/print/electronic publication fields, then `pubYear`. Indexing dates are ignored. Real date parts take precedence over conflicting `pubYear`; year-only metadata remains year-only. Existing Study publication parts are enriched only when compatible, never combined across conflicting dates. Existing historical rows are not automatically repaired because their original date provenance is insufficient. See [ADR-030](docs/architecture/decisions/ADR-030-provider-attempts-and-bounded-responses.md) and the [F15.2 report](docs/development/f15-2-provider-runtime-integrity-hardening-ru.md).

## Evidence Extraction

Extracting first materializes bounded SourceMaterial snapshots for each distinct discovered Study. Search metadata abstracts are retained with provider provenance, and eligible Europe PMC records may add structured JATS full text through the official fullTextXML endpoint. No HTML scraping, arbitrary PDF download, paywall bypass, or publisher crawling is used.

Source selection is deterministic: a current usable non-truncated StructuredFullText snapshot is preferred, then a current Abstract snapshot, otherwise the study receives a persisted NoExtractableText skip and no LLM call. Each completed EvidenceExtraction references the exact SourceMaterial snapshot used. Source content is hashed with SHA-256, historical versions remain available, and changed content creates a new version instead of mutating the source used by older evidence.

The prompt version is `evidence-extractor-v2-source-anchors`. Supporting excerpts must resolve uniquely against the selected SourceMaterial after the versioned `source-text-v1` normalization; the persisted anchor stores canonical offsets, normalized span text, and a SHA-256 span hash. Numeric fields are retained only when deterministic local statistical context verifies the field: effect measure and estimate, CI tuple, p-value/operator, standard error, or conservatively scoped sample size. `Ambiguous` and `Unsupported` facts remain explicit and cannot enter quantitative synthesis. Source scope is preserved as Abstract or StructuredFullText, including truncation metadata.

## Evidence Evaluation

Evaluating uses the exact source scope and grounded Evidence from the current run. Structured full text provides more available methodological information but is not treated as a study-quality score or certainty guarantee. Missing source detail remains Unknown or InsufficientSource, not a negative quality judgment.
## Evidence Corpus and Synthesis

Before Synthesizing, EvidenceCorpusBuilder creates an explicit deterministic application read model over the persisted run-scoped graph. It validates Evidence, EvidenceExtraction, EvidenceEvaluation, and search provenance for the current ResearchRun, verifies Evidence -> EvidenceExtraction -> SourceMaterial -> Study lineage, deduplicates Studies, preserves conflict structure, and calculates descriptive source-coverage metrics. The corpus is then bounded by the Synthesis limits before any LLM call.

Synthesis remains narrative evidence synthesis for persisted ResearchReport claims. Persisted claims may cite only Evidence accepted into the current corpus, and citation metadata is reconstructed from persistence. The system does not average raw EffectValue values; deterministic common/fixed-effect and REML random-effects inverse-variance pooled ratio results are computed only by the Application quantitative synthesis layer when M15 compatibility, independence requirements, and M19 tau-squared estimation requirements are met.
## Evidence Synthesis

`Synthesizing` creates a persisted `ResearchReport` for the current research run. It uses only validated current-run Evidence, current-run Study metadata, search provenance, extraction provenance, and study-level EvidenceEvaluation records.

The prompt version is `research-synthesizer-v2-trusted-evidence`. The synthesis model returns strict structured output, but Application still validates every draft claim. Persisted claims must cite supplied EvidenceIds, cannot provide their own PMID/DOI/study identifiers, and must preserve direction semantics. Citation metadata returned by the API is reconstructed from persisted Evidence and Study rows.

F15.1 rechecks numeric proof against exact immutable source content and binds reported statistics to one explicit local tuple, rather than independent nearby tokens. Quantitative compatibility includes intervention/comparator roles and explicit outcome timepoint; missing context never acts as a wildcard. Raw extraction ResultSummary is excluded from synthesis/evaluation prompts, and unverified numeric fields are nulled before synthesis context construction. Verified describes this bounded deterministic grammar, not universal biomedical entailment. Free-text report numeric grounding remains a limitation. See [ADR-029](docs/architecture/decisions/ADR-029-bound-statistical-tuples-and-trusted-synthesis.md) and the [F15.1 process report](docs/development/f15-1-scientific-trust-boundary-correction-ru.md).

When no validated evidence exists, MedResearch creates an explicit `InsufficientEvidence` report without calling the LLM. Persisted report claims remain narrative and traceable; no vote counting, automatic model selection, modified/ad-hoc HKSJ, tau-squared confidence interval, formal GRADE, formal risk-of-bias result, diagnosis, or treatment recommendation is produced.

## CI

GitHub Actions runs on Ubuntu with Docker available. The workflow restores, builds, runs the full test suite with Testcontainers required, fails if Docker-required CI reports skipped tests, checks for pending EF model changes, validates Docker Compose, and uploads TRX test results for diagnostics. The Testcontainers fixture applies EF migrations to a fresh PostgreSQL database before PostgreSQL integration tests execute. F8 additionally verifies stale stage writers, exact quantitative source lineage, report citation scope, and relational/JSON artifact consistency.

The CI workflow also validates the frontend workspace with deterministic pnpm install, OpenAPI type generation, lint, typecheck, unit/component tests, and a production web build. Normal CI does not run live provider checks and does not require OpenAI, PubMed, Europe PMC, or frontend secrets.

## EF Core Migrations

Restore local tools before running EF commands on a fresh machine:

```bash
dotnet tool restore
```

Create a migration:

```bash
dotnet ef migrations add MigrationName --project src/MedResearch.Infrastructure/MedResearch.Infrastructure.csproj --startup-project src/MedResearch.Api/MedResearch.Api.csproj --output-dir Persistence/Migrations
```

Apply migrations to a configured database:

```bash
dotnet ef database update --project src/MedResearch.Infrastructure/MedResearch.Infrastructure.csproj --startup-project src/MedResearch.Api/MedResearch.Api.csproj
```

## Validation

```bash
dotnet restore
dotnet build
dotnet test
docker compose config
```

Domain, Application, Infrastructure, architecture-boundary, and API tests run without Docker. Planner, evidence extractor, evidence evaluator, and evidence synthesizer tests use fake LLM providers. OpenAI adapter tests use fake HTTP and do not call the live OpenAI API. PubMed adapter tests use local fixtures and fake HTTP for ESearch, EFetch, request parameter, batching, retry, cancellation, XML parsing, and normalization behavior; Europe PMC adapter tests use deterministic fake HTTP for request parameters, cursor pagination, retry, cancellation, JSON parsing, mapping, and deduplication. They do not call the live internet. PostgreSQL integration tests use Testcontainers and run against real PostgreSQL when Docker is reachable. They are skipped locally when Docker is installed but the engine is unavailable; they do not fall back to EF Core InMemory. CI sets `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true`, so Docker/Testcontainers unavailability fails the run instead of silently skipping PostgreSQL coverage. Normal CI does not require `OPENAI_API_KEY`, NCBI credentials, Europe PMC credentials, or live internet calls to OpenAI/PubMed/Europe PMC.

The read-only Evidence & Provenance Explorer is available at `/research/{researchRunId}/evidence` and is backed by `GET /api/research/{researchRunId}/provenance`. It is scoped to the authenticated owner and shows persisted search/discovery paths, canonical Study metadata, SourceMaterial metadata, run-scoped extraction/Evidence/Evaluation, report-claim links, quantitative contribution IDs and F15.2 provider-attempt outcomes. It intentionally never returns raw SourceMaterial content and does not reconstruct citations in the browser. A study can have multiple provider/query discovery paths while remaining one extraction work item for the run. Historical F13 limitations and the newer correction are recorded separately in their process reports.

## Development Notes

For a beginner-friendly architecture map, start with `docs/architecture-overview.md`. For a code-oriented request trace, read `docs/request-lifecycle.md`. For a guided study sequence, read `docs/learning-path.md`.

Read `AGENTS.md`, `ARCHITECTURE.md`, and `docs/development/current-state.md` before significant changes.


### Optional Live PubMed Smoke Test

The optional live PubMed smoke test is intentionally outside `MedResearch.slnx`, so normal `dotnet test` and CI do not call NCBI. To run it explicitly, set `MEDRESEARCH_RUN_LIVE_PUBMED_TESTS=true` and a contact email such as `PubMed__Email` or `PUBMED_EMAIL`, then run:

```bash
dotnet test tests/MedResearch.LivePubMedSmokeTests/MedResearch.LivePubMedSmokeTests.csproj
```

`PubMed__ApiKey` or `PUBMED_API_KEY` is optional. The live smoke test requests one result and is not a load test.
### Optional Live Europe PMC Smoke Test

The optional live Europe PMC smoke test is also outside `MedResearch.slnx`, so normal `dotnet test` and CI do not call Europe PMC. To run it explicitly, set `MEDRESEARCH_RUN_LIVE_EUROPEPMC_TESTS=true`, then run:

```bash
dotnet test tests/MedResearch.LiveEuropePmcSmokeTests/MedResearch.LiveEuropePmcSmokeTests.csproj
```

The live smoke test requests one result through the Europe PMC REST search endpoint and is not a load test.

### Source-material development configuration

SourceAcquisition:MaxStudiesPerRun, SourceAcquisition:MaxContentCharacters, and SourceAcquisition:PreferStructuredFullText bound acquisition. EuropePmcFullText controls the opt-in structured full-text adapter, including timeout, retry, and character limits. A full-text provider failure is logged as operational acquisition failure; unavailable full text falls back to an abstract when one exists and does not fail the research run.

The normal solution tests are deterministic and do not call OpenAI, PubMed, Europe PMC, or live full-text endpoints. Optional live smoke projects are outside MedResearch.slnx and require explicit environment variables.
## Quantitative Evidence Eligibility

Milestone 15 adds a deterministic quantitative-readiness boundary after EvidenceCorpus construction and before any future statistical synthesis. `QuantitativeEvidenceAssessor` consumes a validated run-scoped EvidenceCorpus and produces `QuantitativeEvidenceReadiness`, per-Evidence assessments, and conservative `CompatibleEvidenceGroup` records. This is an Application read model, not a persisted meta-analysis result.

F6 persists the deterministic quantitative result beside the narrative report. `GET /api/research/{researchRunId}/quantitative` reads immutable per-group artifacts containing the complete M17-M24 result, fingerprint, and exact fixed-effect/random-effects contribution snapshots with Evidence lineage. Repeated synthesis is idempotent; a different result for the same run/group is rejected rather than overwriting history. F7 adds a run-scoped quantitative results workspace that presents this artifact without recalculating scientific values. It does not invent study-level intervals or metadata absent from the F6 snapshot.

The layer classifies reported effect-measure labels into explicit types such as odds ratio, risk ratio, hazard ratio, mean difference, standardized mean difference, correlation, and risk difference. It preserves the source-reported `EffectMeasure`, `EffectValue`, confidence interval bounds, p-value, confidence level, and reported standard error separately from deterministic normalized values. The LLM may extract reported statistics from grounded SourceMaterial, but C# code performs all transformations such as `ln(OR)`, confidence-interval-to-SE derivation, variance calculation, and Fisher z for correlations.

Eligibility is conservative. P-values alone do not create effect sizes. Confidence intervals do not create standard errors unless the confidence level is explicitly reported. OR/RR/HR values and their CI bounds must be positive before log transformation. Missing population, comparator, or study-design keys prevent automatic grouping. Multiple Evidence items from one Study are not counted as independent studies. Source truncation is retained as limitation metadata and is not automatic ineligibility when the reported statistic is fully grounded.

Milestone 17 adds `FixedEffectQuantitativeStatisticalSynthesizer`, a deterministic Application read model over M15-compatible groups. V1 supports OR/RR/HR groups only, pools normalized log effects with generic inverse-variance fixed-effect weighting, back-transforms by exponentiation, and exposes the result to synthesis context without persisting a new database table. M19 estimates REML tau-squared as between-study variance foundation data. M22 uses that same tau-squared estimate for a separate REML random-effects inverse-variance pooled estimate with a Wald standard-normal confidence interval. M23 adds canonical HKSJ summary-effect inference beside Wald. M24 adds a Cochrane-style random-effects prediction interval using the M22 point estimate and Wald summary variance plus the M19 REML tau-squared estimate with Student-t `df = k - 1`. It still does not implement modified/ad-hoc HKSJ, tau-squared confidence intervals, forest plots, p-value pooling, vote counting, semantic harmonization, automatic model selection, or formal evidence-certainty claims.

### Optional Live Scientific E2E Validation

Milestone 16 adds an optional live end-to-end validation project outside `MedResearch.slnx`. Normal `dotnet test` and GitHub Actions do not run it and do not call OpenAI, PubMed, Europe PMC, or Europe PMC full-text endpoints. To run it explicitly, provide a disposable PostgreSQL database and live provider configuration:

```bash
MEDRESEARCH_RUN_LIVE_E2E=true
MEDRESEARCH_LIVE_E2E_DATABASE_ACK=isolated
MEDRESEARCH_LIVE_E2E_CONNECTION_STRING="Host=...;Database=medresearch_live_e2e;Username=...;Password=..."
OPENAI_MODEL=<configured-model>
OPENAI_API_KEY=<secret>
PUBMED_EMAIL=<contact-email>
dotnet test tests/MedResearch.LiveE2EValidationTests/MedResearch.LiveE2EValidationTests.csproj
```

The harness uses `WebApplicationFactory<Program>` with production DI and hosted worker processing. It posts one bounded research question to `/api/research`, waits for the normal worker pipeline, and reads `/api/research/{researchRunId}/report`. It applies migrations to the explicitly acknowledged isolated database and overrides runtime bounds to keep validation small: `ResearchPlanning:MaxSearchQueries=2`, scientific source results capped at 5 per query, source acquisition/extraction/evaluation capped at 5 studies, and synthesis capped at 5 studies / 20 findings / 8 claims.

`ResearchPlanning:MaxSearchQueries` is configurable for bounded validation and defaults to 5, preserving the original planner maximum. The live E2E harness intentionally does not run without an OpenAI key and does not print the key or store it in persistence.

## Quantitative Heterogeneity Diagnostics

MedResearch now computes deterministic heterogeneity diagnostics for successful fixed-effect quantitative synthesis groups. The diagnostics are derived by Application code, not by the LLM:

- Cochran's Q uses the same analysis-scale effects, inverse-variance weights, and pooled effect as the fixed-effect synthesis.
- Degrees of freedom are `StudyCount - 1`.
- I-squared is stored as a proportion from `0` to `1` and is bounded at zero when `Q <= df` or `Q == 0`.

These diagnostics do not select a random-effects model, exclude studies, change weights, or explain the clinical/methodological cause of variation. They are transient read-model values in `SynthesisContext`; no persistence table is created for M18.
## Between-Study Variance Foundation

Milestone 19 adds `RestrictedMaximumLikelihoodTauSquaredEstimator`, a pure Application implementation of REML tau-squared estimation for successful fixed-effect quantitative synthesis groups. The estimator consumes the exact same independent Study-level M17 contributions and analysis-scale variances used by the fixed-effect result. For OR/RR/HR groups, tau-squared is therefore on the log-ratio variance scale.

The estimator is deterministic and bounded: it validates finite positive variances, evaluates the REML score at the zero boundary, brackets a non-negative root with a finite upper-bound expansion, and uses bounded bisection. Non-convergence is represented as `BetweenStudyVarianceEstimateStatus.NotEstimated`; it is not silently converted to `0`.

This value is exposed in `SynthesisContext.QuantitativeSyntheses` and the synthesis prompt as supplied MedResearch-computed context. M22 consumes it to compute random-effects weights `1/(vi + tau²)`, a random-effects pooled estimate, variance, SE, and Wald confidence interval. M24 consumes the same tau-squared and M22 summary variance to compute the random-effects prediction interval. It does not change fixed-effect pooled estimates, fixed-effect weights, confidence intervals, Cochran's Q, df, I-squared, Wald random-effects values, or HKSJ values. The normal automated test suite uses deterministic fixtures, including BCG/metafor reference checks, and does not call external statistical services.
