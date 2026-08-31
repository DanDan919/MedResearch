# ADR-007: Source-grounded abstract evidence extraction

Date: 2026-08-31

## Status

Accepted

## Context

MedResearch retrieves PubMed study metadata during `Searching`, but the `Extracting` stage previously produced no evidence. The next step needs structured findings that can later support evaluation and synthesis without treating an LLM as a scientific source.

The approved external input scope for this milestone is intentionally narrow: the current research question, bounded accepted `ResearchPlan` context, and one discovered `Study` title, abstract, and metadata. Full-text papers are not available, and unrelated persisted data must not be sent to the LLM provider.

## Decision

Implement `Extracting` as a sequential Application workflow that loads discovered studies for a `ResearchRun`, calls the existing provider-neutral `IStructuredLlmClient` through `EvidenceExtractor`, validates the structured output locally, and persists source-grounded findings.

Add a versioned extraction prompt, `evidence-extractor-v1`, with strict JSON Schema output. The prompt instructs the model to extract only explicitly reported abstract-level findings and to return null for absent scientific fields.

Persist extraction attempts in a separate `EvidenceExtraction` table. Each row records `ResearchRunId`, `StudyId`, status, optional skip reason, source scope, provider, model, prompt version, extracted timestamp, evidence count, and grounding validation status. A unique constraint on `(research_run_id, study_id, prompt_version)` makes retries idempotent for the same extraction version.

Evolve `Evidence` from a placeholder claim into a run-specific extracted finding. Each finding belongs to a `ResearchRun`, global `Study`, and `EvidenceExtraction`, and stores outcome, result summary, supporting abstract excerpt, effect direction, source scope, timestamp, grounding validation flag, and nullable reported scientific fields such as population, comparator, study design, sample size, effect value, confidence interval bounds, and p-value.

## Grounding Invariant

A persisted `Evidence.SupportingText` value must occur in the supplied source abstract after deterministic normalization. The system does not use a second LLM verifier. Blank, excessive, or fabricated supporting excerpts are rejected before persistence.

Numeric fields are conservative. When a sample size, effect value, confidence interval bound, or p-value is extracted, the same numeric value must be present in the supplied abstract text. Unsupported numeric values are nulled rather than trusted.

## Skips And Failures

A study with no usable abstract is recorded as `EvidenceExtractionStatus.Skipped` with `NoExtractableText` and does not call the LLM provider. This is expected input quality, not a failed run.

Provider failures, malformed structured output, validation failures, and grounding failures propagate to the existing safe `ResearchRun` failure path. Host cancellation continues to propagate as cancellation.

## Consequences

- Evidence is specific to a research run even when studies are globally deduplicated.
- Later synthesis can trace each finding to a study and a bounded abstract excerpt.
- Abstract-level extraction must not be represented as full-paper evidence.
- Changing extraction behavior requires a new prompt/version so prior attempts remain distinguishable.
- The migration reshapes the old placeholder `evidence` table; existing placeholder rows cannot be meaningfully upgraded into source-grounded evidence without source/run provenance.
