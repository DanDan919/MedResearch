# ADR-017: Deterministic Heterogeneity Diagnostics

## Status

Accepted.

## Context

ADR-016 added a deterministic fixed-effect inverse-variance synthesis read model for compatible ratio effects. That result can pool eligible current-run Study contributions, but it did not describe the observed dispersion of the same effects around the pooled estimate.

MedResearch needs a small, testable heterogeneity diagnostic layer before any future random-effects work. The diagnostics must not change the M17 pooled effect, weights, variance, standard error, or confidence interval. They also must not let an LLM invent or alter statistical calculations.

Authoritative methodology reviewed for this decision:

- Cochrane Handbook for Systematic Reviews of Interventions, Chapter 10, on inverse-variance/common-effect meta-analysis and heterogeneity interpretation.
- Higgins, Thompson, Deeks, and Altman, "Measuring inconsistency in meta-analyses", BMJ 2003, for the I-squared formulation and interpretation limitations.

## Decision

Add deterministic heterogeneity diagnostics to successful `QuantitativeSynthesisResult` values:

- Cochran's Q: `sum(w_i * (theta_i - theta_pooled)^2)`.
- Degrees of freedom: `k - 1`, where `k` is the exact number of independent Study contributions in the M17 pooled result.
- I-squared: `0` when `Q <= df` or `Q == 0`; otherwise `(Q - df) / Q`, stored internally as a proportion in the range `0..1`.

The calculation uses:

- the exact M17 contribution set;
- the exact M17 analysis-scale effects;
- the exact M17 inverse-variance weights;
- the exact M17 pooled analysis-scale effect.

The diagnostics are implemented by pure Application code in `HeterogeneityDiagnosticsCalculator` and versioned as `cochran-q-i2-v1`. They are projected into `SynthesisContext` and the synthesis prompt as deterministic application-computed values. The LLM may mention supplied values but must not calculate or replace them.

## Alternatives Considered

1. No heterogeneity diagnostics. Rejected because fixed-effect pooled estimates without dispersion diagnostics give false confidence about what the synthesis layer has actually checked.
2. Jump directly to random-effects meta-analysis. Rejected because tau-squared estimation, random-effects weights, prediction intervals, and model-selection policy are separate decisions.
3. Ask the LLM to describe heterogeneity qualitatively. Rejected because Q and I-squared are arithmetic over validated inputs, not narrative judgments.
4. Add an external statistics package or service. Rejected because Q, df, and I-squared require only basic deterministic arithmetic.

## Consequences

Positive:

- Heterogeneity diagnostics are transparent and independently testable.
- M17 pooled estimates remain unchanged.
- The same Study contribution set, scale, and weights are reused, preventing duplicate-provider provenance from becoming statistical multiplicity.
- I-squared is available to reports without treating it as a quality score or causal explanation.

Limitations:

- No tau-squared, random-effects weights, random-effects pooled estimate, prediction interval, Q p-value, subgroup analysis, forest plot, funnel plot, or publication-bias analysis exists in M18.
- I-squared may be unstable or imprecise with few Studies, including the minimum valid `k = 2` case.
- I-squared does not identify clinical, methodological, or causal reasons for observed dispersion.
- Diagnostics remain a transient read model alongside M17 quantitative synthesis; no database table or migration is added.
