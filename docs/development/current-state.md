# Current State

Date: 2026-09-17

## Exists Now

- Initial repository documentation and development trail.
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
- First end-to-end research API use case:
  - `POST /api/research` creates a `ResearchQuestion` and queued `ResearchRun`.
  - `GET /api/research/{researchRunId}` retrieves the run state and original question.
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
  - Prompt version `evidence-extractor-v1` with strict structured output.
  - LLM input scope is limited to the current question, bounded plan context, and one study title/abstract/metadata item.
  - Studies with no usable abstract are recorded as skipped with `NoExtractableText` and are not sent to the LLM.
  - Supporting excerpts are validated deterministically against the supplied abstract.
  - Numeric fields are persisted only when the same numeric value appears in supplied source text; otherwise they remain null.
  - Provider, malformed output, validation, and grounding failures use the existing safe run failure path.
  - `EvidenceExtraction:MaxStudiesPerRun` defaults to 10 and is bounded between 1 and 50.
- Structured evidence evaluation:
  - `IEvidenceEvaluator` and `EvidenceEvaluator` in Application.
  - `IEvidenceEvaluationStore` implemented by `EfEvidenceEvaluationStore` in Infrastructure.
  - Prompt version `evidence-evaluator-v1` with strict structured output.
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
  - Prompt version `research-synthesizer-v1` with strict structured output.
  - Every persisted completed-report claim must cite supplied EvidenceIds from the same ResearchRun.
  - Citation authority comes from persisted Evidence and Study rows; model-supplied PMID, DOI, and StudyId are rejected.
  - No validated evidence produces a deterministic `InsufficientEvidence` report without an LLM call.
  - Persisted ResearchReport synthesis remains narrative and traceable; deterministic fixed-effect pooled ratio results may be supplied as bounded context, but no random-effects meta-analysis, random-effects pooled estimate, vote counting, formal GRADE, formal RoB, diagnosis, or treatment recommendation is produced.
- Application persistence boundaries:
  - `IResearchStore` for HTTP create/read use cases.
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
  - Application tests for queued run creation, retrieval miss, processing orchestration, planner validation, original-question preservation, planning failure, search behavior, evidence extraction validation, grounding, numeric grounding, skips, deduplication, evidence evaluation validation, source-awareness, no-score enforcement, synthesis context construction, synthesis validation, conflict preservation, insufficient-evidence reports, cancellation, provider failure propagation, and source-level architecture boundaries for Domain/Application.
  - Infrastructure tests for OpenAI Responses API request/response mapping and PubMed parsing/fake HTTP behavior, including request parameters, optional API key handling, batching, retry, cancellation, XML edge cases, and DOI/PMID normalization; Europe PMC fake HTTP behavior, including request parameters, cursor pagination, retry, cancellation, malformed JSON, mapping, deduplication, and PMID/PMCID/DOI normalization.
  - API integration tests using `WebApplicationFactory` and fake stores, so endpoint behavior runs without Docker and does not start hosted services.
  - PostgreSQL integration tests using Testcontainers for research persistence, multiple ResearchRuns per ResearchQuestion, queue semantics, lease recovery, heartbeat, stale-owner fencing, plan/search persistence, multi-search discovery provenance, conservative Study identity edge cases, PMCID identity, hard multi-identifier conflicts, multi-source discovery provenance, extraction deduplication after repeated discovery, evidence extraction persistence, evidence evaluation persistence, report persistence, report relationships, idempotency, authoritative citation reconstruction, shared-Study/run-scoped Evidence citation graphs, fresh migration application, current-run corpus loading, and a full fake-provider vertical pipeline. They run against real PostgreSQL when Docker is reachable and are currently skipped locally because the Docker Desktop engine is unavailable. They do not fall back to EF Core InMemory.
  - GitHub Actions CI requires Docker-backed Testcontainers tests with `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true` and fails on unexpected skipped tests in required-Docker mode.

## Environment Status

- GitHub remote is configured as `https://github.com/DanDan919/MedResearch.git`.
- Docker CLI and Docker Compose are installed, but Docker Desktop engine is not reachable at `//./pipe/dockerDesktopLinuxEngine` in this environment.
- Local Docker Compose health-check runtime verification has not passed because the Docker Compose stack cannot start while the engine is unavailable. `/health/ready` is covered by the PostgreSQL-backed fake-provider integration test when Docker is available.
- Optional live PubMed and Europe PMC smoke test projects exist outside `MedResearch.slnx`; they are not run by default or by normal CI. Normal tests use fixtures and fake HTTP.
- No live OpenAI smoke test is configured or run by default. Normal tests use fake LLM providers and fake HTTP.

## Next Logical Milestone

Keep hardening trust boundaries, retry behavior, provider diagnostics, and identity-conflict observability before adding a third scientific source or another LLM provider. Do not add diagnosis, treatment recommendations, or patient-specific medical advice.

## Not Yet Implemented

- Crossref, OpenAlex, Semantic Scholar, publisher API, or additional non-PubMed/non-Europe-PMC source integrations.
- Additional LLM providers beyond OpenAI.
- Full-text extraction.
- Formal study quality frameworks such as GRADE, RoB 2, ROBINS-I, AMSTAR-2, or NOS.
- Full-text evidence synthesis.
- Random-effects meta-analysis, random-effects pooled estimates, forest plots, p-value pooling, and broad pooled-effect families beyond fixed-effect inverse-variance OR/RR/HR V1. Q/df/I-squared diagnostics exist for successful fixed-effect groups.
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

## Fixed-Effect Quantitative Synthesis V1

- Added `FixedEffectQuantitativeStatisticalSynthesizer` in Application as a deterministic read model over M15 `CompatibleEvidenceGroup` output.
- V1 supports only OR/RR/HR compatible groups with independent Study contributions, finite normalized log effects, and finite positive variance.
- The model computes generic inverse-variance fixed-effect weights, pooled log effect, variance, standard error, configured two-sided confidence interval, and exponentiated reported-scale result.
- Output is transient and exposed through `SynthesisContext.QuantitativeSyntheses`; no database migration or persisted quantitative report table was added.
- The narrative synthesis prompt may receive deterministic pooled results but is forbidden from calculating or altering pooled estimates itself.
- Defaults: `QuantitativeSynthesis:OutputConfidenceLevel=0.95`, `QuantitativeSynthesis:MinimumUniqueStudies=2`.
- Still not implemented: random-effects weights, random-effects pooled estimates, forest plots, MD/SMD/correlation pooling, p-value pooling, semantic outcome harmonization, or cohort-overlap correction.
## Fixed-Effect Heterogeneity Diagnostics V1

Milestone 18 extends the transient quantitative synthesis read model with deterministic heterogeneity diagnostics for successful M17 fixed-effect groups. `HeterogeneityDiagnosticsCalculator` computes Cochran's Q, degrees of freedom, and I-squared using the same M17 contribution set, analysis-scale effects, pooled analysis-scale effect, and inverse-variance weights.

The diagnostics are versioned as `cochran-q-i2-v1` and are projected into `SynthesisContext` and the synthesis prompt. They remain derived read-model data and are not persisted as Evidence, Study metadata, or a database table.

Scope intentionally not implemented: random-effects weights, random-effects pooled estimates, prediction intervals, Q p-values, forest plots, funnel plots, publication-bias tests, subgroup analysis, and automatic model selection.
## REML Between-Study Variance Foundation V1

Milestone 19 extends successful transient quantitative synthesis results with `BetweenStudyVarianceEstimate` using `RestrictedMaximumLikelihoodTauSquaredEstimator`.

- The estimator consumes the exact same independent M17 Study contributions, analysis-scale effects, and positive variances already accepted for fixed-effect synthesis.
- Tau-squared is estimated on the analysis scale; for OR/RR/HR this means squared log-ratio units.
- Boundary `tau² = 0`, failed bracketing, non-finite arithmetic, and max-iteration behavior are explicit rather than hidden behind a fabricated zero.
- The implementation is pure Application code with deterministic finite bracketing and bounded bisection.
- Values are projected into `SynthesisContext` and the synthesis prompt with algorithm version `reml-tau-squared-v1`.
- No migration or persisted quantitative-result table was added.
- M17 fixed-effect pooled values and M18 Q/df/I-squared semantics remain unchanged.
- Still not implemented: random-effects weights, random-effects pooled estimates, HKSJ, prediction intervals, Q-profile tau-squared intervals, automatic model selection, and tau-based I-squared replacement.
