# ADR-021: Random-Effects Prediction Interval

## Status
Accepted

## Context

MedResearch already exposes a deterministic quantitative read-model chain:

- M17 common/fixed-effect inverse-variance OR/RR/HR synthesis.
- M18 Q/df/I-squared diagnostics over the same contribution population.
- M19 REML tau-squared estimation.
- M22 REML random-effects inverse-variance synthesis with Wald standard-normal CI.
- M23 canonical HKSJ summary-effect inference beside Wald.

The next narrow inference object is a random-effects prediction interval. It must not modify the existing M17-M23 values, must not re-estimate tau-squared, and must not let the narrative LLM calculate statistical intervals.

## Decision

Add `RandomEffectsPredictionIntervalCalculator` in Application. It consumes the successful M22 `QuantitativeRandomEffectsSynthesisResult` and reuses:

- `theta_RE`, the M22 random-effects point estimate;
- `Var(theta_RE)`, the M22 Wald summary-effect variance;
- `tau²`, the existing M19 REML between-study variance estimate;
- the exact M22 independent Study contribution population;
- the configured `QuantitativeSynthesis:OutputConfidenceLevel`.

For `k >= 2`:

```text
df = k - 1
prediction_variance = Var(theta_RE) + tau²
prediction_SE = sqrt(prediction_variance)
PI = theta_RE +/- t_(1-alpha/2, df) * prediction_SE
```

For OR/RR/HR groups, arithmetic remains on the natural-log analysis scale. Reported-scale point estimates and prediction interval endpoints are produced with `exp(...)` only after the analysis-scale interval is complete.

The prediction interval is exposed as `QuantitativeRandomEffectsSynthesisResult.PredictionInterval` beside:

- the existing M22 Wald result;
- the existing M23 `HksjInference`.

The Wald and HKSJ values remain unchanged.

## Boundary Behavior

- `k = 1` returns `NotSynthesizable` with `df = 0`.
- `tau² = 0` is valid. In that case `prediction_variance` collapses to the M22 Wald summary-effect variance, while the prediction interval still uses Student-t critical values for the selected method. This does not change the existing standard-normal Wald CI.
- non-finite point estimates, tau-squared, summary variances, prediction variances, interval endpoints, or back-transformed endpoints return explicit unavailable results.

## Consequences

- `SynthesisContext` projects deterministic prediction interval values beside Wald and HKSJ.
- `ResearchSynthesisPrompt` carries those values to synthesis and explicitly forbids the LLM from calculating, changing, selecting, or inferring prediction intervals.
- No database schema change is required because the result is a transient deterministic Application read model.

## Deliberately Not Implemented

- modified/ad-hoc HKSJ;
- prediction interval model selection or recommendation;
- prediction intervals for unsupported effect families;
- tau-squared confidence intervals;
- prediction intervals that use alternative df conventions or Riley-style variants;
- p-values, forest plots, subgroup analysis, meta-regression, publication-bias methods;
- persisted quantitative result artifacts.

These require separate methodology decisions and regression tests.
