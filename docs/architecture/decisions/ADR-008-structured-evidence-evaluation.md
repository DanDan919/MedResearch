# ADR-008: Structured source-aware evidence evaluation

Date: 2026-08-31

## Status

Accepted

## Context

MedResearch now has source-grounded abstract-level `Evidence` and `EvidenceExtraction` provenance. The next pipeline stage needs individual study/evidence evaluation without pretending that abstracts provide enough detail for full risk-of-bias assessment.

The evaluator may use the configured structured LLM provider, but the LLM remains an untrusted interpretation aid. The user approved sending only current-run scientific context needed for evaluation: current question, bounded plan context, relevant study metadata/source text, grounded evidence findings, supporting excerpts, and extraction provenance. It is not authorized to receive unrelated records, secrets, logs, or unrelated runs.

## Decision

Add `EvidenceEvaluation` as a study-level evaluation entity for one `ResearchRun`, one `Study`, and one evaluator prompt version. It stores the evaluated `EvidenceIds` so future synthesis can connect study-level methodology with the exact grounded findings considered.

Use one study-level evaluation instead of one duplicated evaluation per evidence row. Finding-level information is represented through directness, precision, comparator and signal fields derived from all validated findings for the study. This keeps methodology from being copied across five findings while remaining useful to future synthesis.

Use bounded categorical domains instead of a numeric quality score:

- `StudyDesignClassification`
- `MethodologicalAssessmentState`
- `ComparatorPresence`
- `DirectnessRating`
- `MethodologicalConfidence`

Persist evaluator provenance directly on `EvidenceEvaluation`: provider, model, prompt version, evaluated timestamp, source scope, status, and optional skip reason. A unique constraint on `(research_run_id, study_id, prompt_version)` makes retries idempotent.

## Semantics

`Unknown` means MedResearch cannot determine the value from available validated information.

`InsufficientSource` means the current source scope is not adequate for that methodological judgment.

`NotApplicable` means the domain does not conceptually apply to the study design/context.

`SomeConcern` and `SeriousConcern` require an actual source-supported methodological concern. Absence of abstract detail must not be converted into a negative quality judgment.

## Hybrid Evaluation

The evaluator first builds deterministic signals from trusted stored facts: source scope, evidence count, sample-size presence, comparator presence, effect-estimate presence, confidence-interval presence, p-value presence, and cautious publication/source text design hints.

The LLM then receives a bounded prompt, `evidence-evaluator-v1`, and strict schema. It may classify source-supported methodological signals and directness relative to the current research question. Application validation checks identities, categories, bounded text, author-reported limitation grounding, source-scope semantics, and statistical-significance safeguards.

For abstract-only source scope, domains such as allocation concealment, detailed attrition assessment, and often blinding are finalized as `InsufficientSource` or `NotApplicable` unless source text supports a more specific judgment.

## Skips And Failures

If no extracted evidence exists for a study, the evaluator records a skipped evaluation with `NoExtractedEvidence` and does not call the LLM.

Provider failures, malformed structured output, validation failures, and unsupported methodological claims propagate to the existing safe `ResearchRun` failure path. Expected scientific insufficiency is represented in structured categories and does not fail the run.

## Not Formal GRADE Or Risk Of Bias

This is an internal structured assessment layer. It is not GRADE, Cochrane RoB 2, ROBINS-I, AMSTAR-2, Newcastle-Ottawa Scale, or another validated framework. Future work may add formal framework-specific evaluators, but this milestone deliberately avoids fake framework claims and arbitrary weighted scoring.

## Consequences

- Future synthesis can combine evidence findings with study-level methodological context without parsing prose.
- The model preserves source limitations explicitly instead of collapsing them into quality concerns.
- Overall confidence is categorical and source-aware, not a hidden formula or validated clinical grade.
- Changing evaluation behavior requires a new prompt/version so prior attempts remain distinguishable.
