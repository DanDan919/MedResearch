# Learning path

This path is for a developer who wants to understand MedResearch without reading every file in random order. Each level has a goal, files to read, mechanisms to understand, questions, and one tracing exercise.

## Level 1: What happens after POST?

Goal: understand why `POST /api/research` returns quickly and what is persisted immediately.

Read:

- `src/MedResearch.Api/Program.cs`
- `src/MedResearch.Api/Research/ResearchApiModels.cs`
- `src/MedResearch.Application/Research/CreateResearchUseCase.cs`
- `src/MedResearch.Domain/ResearchQuestion.cs`
- `src/MedResearch.Domain/ResearchRun.cs`
- `src/MedResearch.Infrastructure/Research/EfResearchStore.cs`

Mechanism:

A request creates a `ResearchQuestion` and a `ResearchRun`. It does not perform research inline.

Questions:

1. Why is `ResearchQuestion` separate from `ResearchRun`?
2. Which code rejects an empty question?
3. Why does the API return `Queued` instead of waiting for `Completed`?

Exercise:

Trace a fake question from `CreateResearchRequest` to the two EF entities inserted by `PersistInitialResearchAsync`.

## Level 2: Run lifecycle and leases

Goal: understand how MedResearch safely processes background work and recovers after crashes.

Read:

- `src/MedResearch.Domain/ResearchRun.cs`
- `src/MedResearch.Domain/ResearchRunStatus.cs`
- `src/MedResearch.Infrastructure/Research/Processing/BackgroundResearchWorker.cs`
- `src/MedResearch.Application/Research/Processing/ResearchRunProcessor.cs`
- `src/MedResearch.Application/Research/Processing/IResearchRunQueue.cs`
- `src/MedResearch.Infrastructure/Research/Processing/PostgreSqlResearchRunQueue.cs`
- `tests/MedResearch.IntegrationTests/ResearchRunQueueConcurrencyTests.cs`

Mechanism:

PostgreSQL claim/reclaim uses `FOR UPDATE SKIP LOCKED`, lease owner, lease expiry, heartbeat, and lease version fencing.

Questions:

1. Which statuses can be reclaimed after lease expiry?
2. What prevents an old worker from saving progress after another worker reclaimed the run?
3. Why should the worker not keep a database transaction open while calling external providers?

Exercise:

Follow a run from `Queued` through claim into `Planning`, then identify the exact SQL conditions used by `SaveProgressAsync`.

## Level 3: Planning boundary

Goal: understand how LLM planning is allowed to influence the system and where it is constrained.

Read:

- `src/MedResearch.Application/Research/Planning/ResearchPlanner.cs`
- `src/MedResearch.Application/Research/Planning/ResearchPlannerPrompt.cs`
- `src/MedResearch.Application/Research/Planning/ResearchPlanDraft.cs`
- `src/MedResearch.Application/Research/Planning/ResearchPlanValidator.cs`
- `src/MedResearch.Infrastructure/Planning/Persistence/EfResearchPlanStore.cs`
- `tests/MedResearch.Application.Tests/ResearchPlannerTests.cs`

Mechanism:

LLM output is a draft. Application validates it before creating persisted `ResearchPlan` state.

Questions:

1. Why must `originalQuestion` match the stored question?
2. What happens if the planner invents DOI or PMID identifiers?
3. Where is the maximum number of search queries configured?

Exercise:

Find the validator rule that rejects a bad planner response, then find the test that proves it.

## Level 4: Multi-source scientific search

Goal: understand PubMed/Europe PMC search without confusing provider records with canonical Study identity.

Read:

- `src/MedResearch.Application/Research/Literature/ScientificLiteratureSearchCoordinator.cs`
- `src/MedResearch.Application/Research/Literature/ScientificSearchContracts.cs`
- `src/MedResearch.Infrastructure/Literature/Persistence/EfScientificSearchResultStore.cs`
- `src/MedResearch.Infrastructure/Literature/Identity/ScientificIdentifierNormalizer.cs`
- `src/MedResearch.Infrastructure/Literature/PubMed/PubMedScientificLiteratureSource.cs`
- `src/MedResearch.Infrastructure/Literature/EuropePmc/EuropePmcScientificLiteratureSource.cs`
- `tests/MedResearch.IntegrationTests/ScientificSearchPersistenceTests.cs`

Mechanism:

Each provider/query execution gets its own `LiteratureSearch`. Candidates are normalized to canonical `Study` using stable identifiers only.

Questions:

1. Why is uniqueness `(literature_search_id, study_id)` instead of `(research_run_id, study_id)`?
2. Why does MedResearch not merge studies by title?
3. What happens when PMID and DOI point to different existing Studies?

Exercise:

Trace one PubMed candidate and one Europe PMC candidate with the same PMID into one `Study` and two discoveries.

## Level 5: Source material

Goal: understand why extraction uses `SourceMaterial`, not raw Study metadata alone.

Read:

- `src/MedResearch.Domain/SourceMaterial.cs`
- `src/MedResearch.Application/Research/SourceMaterials/SourceMaterialAcquirer.cs`
- `src/MedResearch.Application/Research/SourceMaterials/SourceMaterialContracts.cs`
- `src/MedResearch.Infrastructure/SourceMaterials/Persistence/EfSourceMaterialStore.cs`
- `src/MedResearch.Infrastructure/SourceMaterials/EuropePmc/EuropePmcFullTextSourceMaterialProvider.cs`

Mechanism:

MedResearch stores exact text snapshots with hash/version/current metadata and later points completed extraction to the exact source snapshot.

Questions:

1. Why is content versioning needed?
2. What is the difference between abstract fallback and provider failure?
3. Why is structured full text not automatically a quality score?

Exercise:

Find the source-selection ordering used before extraction and explain which source wins when both abstract and non-truncated structured full text exist.

## Level 6: Evidence extraction

Goal: understand source-grounded findings and the extraction trust boundary.

Read:

- `src/MedResearch.Application/Research/Extraction/EvidenceExtractor.cs`
- `src/MedResearch.Application/Research/Extraction/EvidenceExtractionPrompt.cs`
- `src/MedResearch.Application/Research/Extraction/EvidenceExtractionDraftValidator.cs`
- `src/MedResearch.Application/Research/Extraction/EvidenceGroundingValidator.cs`
- `src/MedResearch.Application/Research/Extraction/EvidenceNumericGroundingValidator.cs`
- `src/MedResearch.Infrastructure/Extraction/Persistence/EfEvidenceExtractionStore.cs`
- `tests/MedResearch.Application.Tests/EvidenceExtractionTests.cs`

Mechanism:

LLM proposes findings, but supporting excerpts and numeric fields must be grounded in the selected source text.

Questions:

1. Why is an extraction with no source material skipped instead of sent to the LLM?
2. Which fields are required for a finding?
3. Why can a numeric value extracted by the model be persisted as null?

Exercise:

Trace one accepted finding into an `EvidenceExtraction` row and one `Evidence` row.

## Level 7: Evidence evaluation

Goal: understand source-aware methodological assessment without overstating it as formal quality grading.

Read:

- `src/MedResearch.Application/Research/Evaluation/EvidenceEvaluator.cs`
- `src/MedResearch.Application/Research/Evaluation/EvidenceEvaluationSignalBuilder.cs`
- `src/MedResearch.Application/Research/Evaluation/EvidenceEvaluationDraftValidator.cs`
- `src/MedResearch.Infrastructure/Evaluation/Persistence/EfEvidenceEvaluationStore.cs`
- `tests/MedResearch.Application.Tests/EvidenceEvaluationTests.cs`

Mechanism:

Evaluation is categorical and source-aware. Missing source detail stays `Unknown` or `InsufficientSource`.

Questions:

1. Why does a study with no extracted evidence skip the LLM call?
2. What is the difference between `Unknown`, `InsufficientSource`, and `NotApplicable`?
3. Why is this not GRADE, RoB 2, ROBINS-I, AMSTAR-2, or NOS?

Exercise:

Find how evaluated `EvidenceIds` are stored and later used by synthesis corpus validation.

## Level 8: EvidenceCorpus and synthesis

Goal: understand why report claims can be trusted to cite current-run evidence.

Read:

- `src/MedResearch.Application/Research/Synthesis/EvidenceCorpusBuilder.cs`
- `src/MedResearch.Application/Research/Synthesis/SynthesisContextBuilder.cs`
- `src/MedResearch.Application/Research/Synthesis/ResearchSynthesizer.cs`
- `src/MedResearch.Application/Research/Synthesis/ResearchReportDraftValidator.cs`
- `src/MedResearch.Infrastructure/Synthesis/Persistence/EfResearchSynthesisStore.cs`
- `tests/MedResearch.Application.Tests/ResearchSynthesisTests.cs`

Mechanism:

The corpus validates run scope and source lineage before a bounded context reaches the synthesis LLM. Claims must cite supplied EvidenceIds.

Questions:

1. Which validation rejects cross-run Evidence in synthesis context?
2. Why are model-supplied PMID/DOI/StudyId rejected?
3. How does the report endpoint reconstruct citation metadata?

Exercise:

Trace one persisted claim from `ResearchReportDraft` to `ResearchReportClaimEvidence`, then through the report read model to returned citation metadata.

## Level 9: Quantitative read models

Goal: understand what M15-M22 added and what remains explicitly out of scope.

Read:

- `src/MedResearch.Application/Research/Quantitative/QuantitativeEvidenceAssessor.cs`
- `src/MedResearch.Application/Research/Quantitative/QuantitativeEvidenceContracts.cs`
- `src/MedResearch.Application/Research/Quantitative/FixedEffectQuantitativeStatisticalSynthesizer.cs`
- `src/MedResearch.Application/Research/Quantitative/HeterogeneityDiagnosticsCalculator.cs`
- `src/MedResearch.Application/Research/Quantitative/RestrictedMaximumLikelihoodTauSquaredEstimator.cs`
- `src/MedResearch.Application/Research/Quantitative/RandomEffectsQuantitativeStatisticalSynthesizer.cs`
- `tests/MedResearch.Application.Tests/QuantitativeEvidenceAssessorTests.cs`
- `tests/MedResearch.Application.Tests/QuantitativeStatisticalSynthesizerTests.cs`
- `tests/MedResearch.Application.Tests/HeterogeneityDiagnosticsCalculatorTests.cs`
- `tests/MedResearch.Application.Tests/RestrictedMaximumLikelihoodTauSquaredEstimatorTests.cs`

Mechanism:

Quantitative code is deterministic Application logic over grounded current-run evidence. It is not LLM-generated and not persisted as a report table.

Questions:

1. Why are p-values alone not enough to create an effect size?
2. Why does fixed-effect V1 support OR/RR/HR but not every measure family?
3. Why does M22 reuse M19 REML tau-squared instead of re-estimating tau-squared?

Exercise:

Pick one eligible OR/RR/HR evidence group and trace how it becomes a fixed-effect result, Q/I-squared diagnostics, REML tau-squared estimate, and REML random-effects Wald result in `SynthesisContext`.

## Level 10: Persistence and migrations

Goal: understand how EF Core maps the graph to PostgreSQL and where constraints live.

Read:

- `src/MedResearch.Infrastructure/Persistence/MedResearchDbContext.cs`
- `src/MedResearch.Infrastructure/Persistence/Configurations/*.cs`
- `src/MedResearch.Infrastructure/Persistence/Migrations/*.cs`
- `docs/architecture/decisions/ADR-002-postgresql-ef-core-persistence.md`
- `docs/architecture/decisions/ADR-003-application-persistence-boundary.md`

Mechanism:

Infrastructure owns EF Core mappings and migrations. Application owns use-case-shaped persistence ports. Domain does not know EF Core.

Questions:

1. Which invariants are enforced by PostgreSQL unique indexes?
2. Which invariants are enforced by Application validation and tests rather than pure database constraints?
3. Why should migrations be forward-only instead of editing old migration history?

Exercise:

Find the EF configuration for `research_report_claim_evidence` and explain how it enforces citation existence but not by itself same-run citation scope.

## Suggested reading order

1. `docs/architecture-overview.md`
2. `docs/request-lifecycle.md`
3. `README.md`
4. `ARCHITECTURE.md`
5. Relevant ADRs under `docs/architecture/decisions/`
6. Tests for the area you want to modify
7. Production code for that area

## Rule of thumb for future changes

Before adding a feature, identify:

- which layer owns it;
- whether data is trusted or untrusted;
- whether it is global `Study` state or run-scoped state;
- what provenance must be persisted;
- what a retry would duplicate;
- what test would fail if the invariant breaks.
