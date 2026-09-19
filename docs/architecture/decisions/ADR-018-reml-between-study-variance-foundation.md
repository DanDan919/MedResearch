# ADR-018: REML Between-Study Variance Foundation

## Status

Accepted.

## Context

Milestones 15, 17, and 18 established a deterministic quantitative path:

- source-grounded Evidence may contain reported quantitative statistics;
- Application code normalizes eligible OR/RR/HR findings onto an analysis scale;
- `FixedEffectQuantitativeStatisticalSynthesizer` computes a fixed-effect inverse-variance result;
- `HeterogeneityDiagnosticsCalculator` computes Cochran's Q, degrees of freedom, and Q-derived I-squared from the same contribution set.

The next useful statistical building block is an estimate of between-study variance, tau-squared. The Cochrane Handbook describes tau-squared as the estimate of between-study variance, notes that REML is available in RevMan as of 2024, and says RevMan's default between-study variance estimator is REML. The `metafor` package documents `method="REML"` and publishes the BCG example where `rma(yi, vi, method="REML")` reports tau-squared approximately 0.3132.

However, MedResearch does not yet implement random-effects weights, a random-effects pooled estimate, HKSJ confidence intervals, prediction intervals, or model selection. Adding tau-squared must not imply those downstream methods exist.

## Decision

Add `RestrictedMaximumLikelihoodTauSquaredEstimator` in Application as pure deterministic C# code.

The estimator:

- consumes only validated `QuantitativeSynthesisContribution` values;
- uses the exact same independent Study-level contribution set as M17/M18;
- estimates tau-squared on the analysis scale, so current OR/RR/HR groups estimate variance on the squared log-ratio scale;
- evaluates the constrained boundary at `tau² = 0` first;
- uses finite upper-bound expansion to bracket a non-negative root of the REML score;
- uses bounded bisection rather than Newton/Fisher scoring for deterministic convergence behavior;
- returns `NotEstimated` for invalid input, non-finite arithmetic, failed bracketing, or max-iteration failure.

The REML score equation used by the implementation is:

```text
w_i = 1 / (v_i + tau²)
mu_hat = sum(w_i * y_i) / sum(w_i)
score = sum(w_i² * (y_i - mu_hat)²) - sum(w_i) + sum(w_i²) / sum(w_i)
```

A boundary result of tau-squared `0` is a valid estimate when the score at zero is not positive. Non-convergence is represented separately and must not be silently converted to zero.

Add `BetweenStudyVarianceEstimate` to `QuantitativeSynthesisResult` and project it into `SynthesisContext.QuantitativeSyntheses` and the synthesis prompt. The prompt forbids the LLM from calculating or replacing tau-squared, random-effects weights, random-effects pooled estimates, confidence intervals, p-values, or effect sizes.

Do not add a database migration. The quantitative output remains a transient read model reproducible from persisted Evidence lineage plus algorithm versions.

## Consequences

Positive:

- MedResearch gains deterministic between-study variance foundation data without expanding into random-effects inference prematurely.
- The implementation is isolated from Domain, Infrastructure, API, EF Core, PostgreSQL, OpenAI, filesystem, and network concerns.
- Reference tests can verify the numerical method against `metafor` BCG output.
- Existing M17 fixed-effect outputs and M18 Q/df/I-squared semantics remain stable.

Negative / deferred:

- No random-effects pooled estimate exists yet.
- No random-effects weights, HKSJ interval, prediction interval, Q-profile tau-squared interval, model selection, or tau-based I-squared replacement exists yet.
- The bisection implementation favors deterministic robustness over iteration speed.
- Persisted quantitative result snapshots remain future work if API/reporting requirements need them.

## References

- Cochrane Handbook, Chapter 10, section 10.10.4: https://www.cochrane.org/authors/handbooks-and-manuals/handbook/current/chapter-10
- metafor `rma.uni` documentation and BCG example: https://wviechtb.github.io/metafor/reference/rma.uni.html
