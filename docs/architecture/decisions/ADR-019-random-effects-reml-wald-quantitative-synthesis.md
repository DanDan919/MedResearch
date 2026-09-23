# ADR-019: Random-Effects REML/Wald Quantitative Synthesis

## Status
Accepted

## Context

MedResearch already had a deterministic quantitative read-model chain:

- M15 classifies eligible quantitative evidence and compatible study-level groups.
- M17 computes common/fixed-effect inverse-variance syntheses for compatible OR/RR/HR evidence.
- M18 computes Cochran's Q, degrees of freedom, and I-squared over the same contribution population.
- M19 estimates between-study variance with a REML tau-squared estimator.

M19 deliberately stopped before calculating random-effects weights or a pooled random-effects estimate. The next required step is to use the existing REML tau-squared estimate to expose a deterministic random-effects synthesis without changing earlier common-effect, heterogeneity, or tau-squared semantics.

## Decision

Add a separate transient random-effects result beside the existing common/fixed-effect result.

For each synthesized compatible group, MedResearch now calculates:

```text
w_i_RE = 1 / (v_i + tau^2)
theta_RE = sum(w_i_RE * theta_i) / sum(w_i_RE)
Var(theta_RE) = 1 / sum(w_i_RE)
SE(theta_RE) = sqrt(Var(theta_RE))
CI_Wald = theta_RE +/- z * SE(theta_RE)
```

For odds ratios, risk ratios, and hazard ratios, arithmetic remains on the natural-log analysis scale. Reported-scale point estimates and confidence interval endpoints are back-transformed with `exp(...)`.

The random-effects result consumes the M19 `BetweenStudyVarianceEstimate`; it does not contain another tau-squared estimator and does not copy or modify REML logic.

## Consequences

- The M17 common/fixed-effect result remains available and unchanged.
- The M18 Q/df/I-squared diagnostics remain unchanged.
- The M19 REML tau-squared estimate remains the single authoritative between-study variance estimate.
- The M22 random-effects result is unavailable when tau-squared is not estimated.
- `tau^2 = 0` is a valid estimate; in that case random-effects weights and Wald synthesis collapse to the same values as the common/fixed-effect inverse-variance result for the same contribution population.
- No database schema change is required because quantitative synthesis remains a deterministic Application read model.

## Deliberately Not Implemented

- Hartung-Knapp-Sidik-Jonkman inference.
- Modified/ad-hoc HKSJ.
- Prediction intervals.
- Tau-squared confidence intervals.
- Automatic model selection based on I-squared, Q, tau-squared, or significance.
- Subgroup analysis, meta-regression, or publication-bias methods.

These require separate methodology decisions and regression tests.
