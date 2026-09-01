# ADR-009: Traceable Evidence Synthesis Reports

Date: 2026-09-01

## Status

Accepted

## Context

MedResearch now has a pipeline that can persist research questions, run lifecycle state, structured plans, PubMed search provenance, source-grounded abstract evidence, and source-aware methodological evaluations. The `Synthesizing` stage previously avoided fake conclusions by doing no meaningful synthesis.

The next step needs a real report artifact, but the scientific safety constraints remain strict:

- LLM output is untrusted.
- Every substantive scientific claim must be traceable to persisted, validated Evidence from the same ResearchRun.
- Abstract-level evidence must not be presented as full-paper evidence.
- Missing scientific data must stay missing.
- Source absence must not become a methodological flaw.
- MedResearch must not present internal categorical assessments as formal GRADE, RoB 2, ROBINS-I, AMSTAR-2, NOS, or another validated framework.

## Decision

Add a persisted `ResearchReport` aggregate shape for traceable evidence synthesis.

Application builds a deterministic `SynthesisContext` from the current ResearchRun only. Infrastructure loads the corpus from PostgreSQL, but Application validates run identity, discovered-study membership, evidence identity, search provenance, extraction provenance, and evaluation provenance before synthesis.

The synthesis model receives only bounded current-run context:

- authoritative research question;
- accepted plan context when present;
- persisted search provenance summary;
- discovered studies with selected validated Evidence;
- study-level EvidenceEvaluation context;
- deterministic outcome-direction summaries;
- deterministic source-coverage and limitation statements.

The model must return strict structured JSON using prompt version `research-synthesizer-v1`. Application validates the draft and persists only accepted claims.

Persisted report claims are stored separately from citations. Each persisted `ResearchReportClaim` must cite one or more `EvidenceId` values. Citation metadata exposed by the API is reconstructed from authoritative persisted `Evidence` and `Study` rows, not from model-supplied PMID, DOI, title, or study id values.

The synthesis result is qualitative. Direction counts are descriptive corpus context only. They are not vote counts, statistical weights, pooled effects, certainty estimates, or meta-analysis output.

If there are no validated evidence findings in the current run, MedResearch creates a deterministic `InsufficientEvidence` report and does not call the LLM provider.

## Consequences

New persistence tables are introduced:

- `research_reports`
- `research_report_claims`
- `research_report_claim_evidence`

A unique `(research_run_id, prompt_version)` constraint makes report persistence idempotent for a prompt version.

The API exposes:

```text
GET /api/research/{researchRunId}/report
```

The endpoint returns the persisted report, coverage metadata, deterministic limitations, claims, and authoritative citations. It returns `404` for unknown runs and `409` when a known run has no report yet.

The pipeline now completes only after the synthesis report has been persisted or an explicit insufficient-evidence report has been created.

## Tradeoffs

The first synthesis implementation intentionally avoids semantic outcome harmonization beyond exact normalized outcome labels. This prevents over-merging distinct outcomes, but it may miss conceptually related findings.

The system records limitations for possible cohort overlap and systematic-review/primary-study citation overlap, but it does not detect or resolve overlap yet.

No full-text synthesis, RAG, formal risk-of-bias framework, formal evidence certainty framework, or meta-analysis is implemented.

## Verification

The implementation is covered by Application tests for context construction, run scoping, conflict detection, draft validation, provider failure propagation, cancellation, and insufficient-evidence behavior. API tests cover report readback, citations, not-ready behavior, unknown runs, and insufficient-evidence reports. PostgreSQL integration tests cover report persistence, idempotency, relationships, authoritative citation reconstruction, and current-run corpus loading when Docker/PostgreSQL is available.