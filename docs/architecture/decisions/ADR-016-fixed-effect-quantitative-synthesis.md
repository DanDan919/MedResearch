# ADR-016: Deterministic Fixed-Effect Quantitative Synthesis V1

Status: Accepted
Date: 2026-09-17

## Context

Milestone 15 added quantitative eligibility over validated EvidenceCorpus, but it intentionally did not pool effects. MedResearch now has enough deterministic prerequisites for one narrow statistical model: compatible current-run Evidence groups with normalized log ratio effects, finite uncertainty, and independent Study contributions.

The statistical layer must not move into Domain, Infrastructure, or the LLM prompt. LLM output may extract source-reported statistics when grounded in SourceMaterial, but Application code must perform authoritative statistical normalization and pooling.

Methodology references consulted include the Cochrane Handbook sections on inverse-variance meta-analysis and ratio-measure analysis on logarithmic scales. The implementation also uses a deterministic standard-normal inverse approximation for confidence interval critical values.

## Decision

Add `FixedEffectQuantitativeStatisticalSynthesizer` in Application. It consumes `QuantitativeEvidenceReadiness` and returns a transient `QuantitativeSynthesisReadiness` read model.

V1 supports only compatible ratio measures:

- odds ratio
- risk ratio
- hazard ratio

For each compatible group the synthesizer requires:

- at least `QuantitativeSynthesis:MinimumUniqueStudies` independent Studies, default 2;
- no dependent multiple Evidence from the same Study;
- M15-eligible Evidence only;
- finite normalized log-scale effect;
- finite positive variance.

The algorithm computes generic inverse-variance fixed-effect weights:

```text
weight_i = 1 / variance_i
pooled_log_effect = sum(weight_i * log_effect_i) / sum(weight_i)
pooled_variance = 1 / sum(weight_i)
pooled_se = sqrt(pooled_variance)
ci = pooled_log_effect +/- z(confidence_level) * pooled_se
reported_scale = exp(log_scale)
```

The default output confidence level is 0.95. Configuration rejects confidence levels outside `(0, 1)` and minimum unique-study counts below 2.

Expose successful results through `SynthesisContext.QuantitativeSyntheses` before the narrative synthesis LLM call. The prompt may include supplied deterministic values, but it instructs the model not to calculate, alter, or invent pooled statistics.

Do not persist pooled results in V1. Persisted Evidence, EvidenceExtraction, SourceMaterial, Study, and EvidenceEvaluation lineage plus the algorithm version `fixed-effect-inverse-variance-v1` are sufficient to reproduce the read model. A future durable quantitative result table can be added when API/report contracts need machine-readable pooled artifacts.

## Alternatives Considered

- Persisting pooled results immediately: rejected because the result is deterministic over already persisted traceable inputs and no stable public quantitative report contract exists yet.
- Letting the LLM compute pooled statistics: rejected because statistical synthesis must be deterministic and testable.
- Averaging raw OR/RR/HR values: rejected because ratio measures must be pooled on the log scale.
- Supporting mean differences, standardized mean differences, correlations, or risk differences in V1: deferred because each family needs explicit scale semantics and tests.
- Adding random-effects modeling, I-squared/Q, tau-squared, forest plots, subgroup analysis, or meta-regression now: rejected as broader than the first narrow statistical model.

## Consequences

MedResearch can now provide a first deterministic pooled ratio estimate for compatible current-run evidence without weakening provenance or LLM trust boundaries.

The result is a fixed-effect/common-effect model assumption. It does not prove homogeneity, account for between-study heterogeneity, resolve cohort overlap, or replace formal systematic-review judgment. Reports and documentation must keep those limitations explicit.

Normal tests remain deterministic and do not require OpenAI, PubMed, Europe PMC, or live network access. PostgreSQL/Testcontainers confidence remains in CI for the persisted graph, while the M17 statistical engine is Application unit-tested.