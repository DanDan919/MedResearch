# ADR-020: Canonical HKSJ Summary-Effect Inference

## Status
Accepted

## Context

M17-M22 created a deterministic quantitative chain for compatible OR/RR/HR evidence groups: common/fixed-effect synthesis, Q/df/I-squared diagnostics, REML tau-squared, and REML random-effects Wald synthesis. The next reliability requirement is to expose Hartung-Knapp-Sidik-Jonkman inference for the random-effects summary effect without replacing the existing Wald result or letting the narrative LLM calculate statistical intervals.

`metafor::rma.uni` documents `test="knha"`/`test="hksj"` as Knapp-Hartung/Hartung-Knapp-Sidik-Jonkman inference that adjusts coefficient standard errors and uses t-distribution degrees of freedom `k - p`. MedResearch's M23 implementation is intercept-only, so `p = 1` and `df = k - 1`.

## Decision

Add `HksjSummaryEffectInferenceCalculator` in Application. It consumes the M22 `QuantitativeRandomEffectsSynthesisResult` and reuses its point estimate, random-effects weights, Wald variance, study contributions, confidence level, and effect-measure scale.

For a successful random-effects result with `k >= 2`, MedResearch calculates:

```text
df = k - 1
q_HKSJ = sum(w_i_RE * (theta_i - theta_RE)^2) / df
Var_HKSJ(theta_RE) = q_HKSJ * Var_Wald(theta_RE)
SE_HKSJ = sqrt(Var_HKSJ)
CI_HKSJ = theta_RE +/- t_(1-alpha/2, df) * SE_HKSJ
```

OR/RR/HR reported-scale HKSJ values are produced by exponentiating the analysis-scale point estimate and HKSJ interval endpoints only after all analysis-scale arithmetic is complete.

The existing M22 random-effects Wald fields remain unchanged and keep `ConfidenceIntervalMethod.WaldStandardNormal`. HKSJ is exposed explicitly beside Wald through `QuantitativeRandomEffectsSynthesisResult.HksjInference` with `ConfidenceIntervalMethod.HartungKnappSidikJonkman`.

M23 implements canonical HKSJ only. It does not implement modified/ad-hoc HKSJ. Therefore, when the HKSJ variance adjustment is below 1, the HKSJ interval may be narrower than the Wald interval.

## Consequences

- HKSJ is deterministic Application read-model output and is not persisted in a database table.
- `SynthesisContext` projects HKSJ beside the existing Wald random-effects result.
- `ResearchSynthesisPrompt` forbids the LLM from calculating, changing, selecting, or inferring HKSJ values.
- `k = 1` is explicitly unavailable with `df = 0`.
- M17 fixed-effect output, M18 Q/df/I-squared semantics, M19 tau-squared, and M22 Wald random-effects output remain unchanged.
- No database migration is required.

## Deliberately Not Implemented

- Modified/ad-hoc HKSJ.
- Prediction intervals.
- Tau-squared confidence intervals.
- Quantitative p-values.
- Automatic inference/model selection.
- Persisted quantitative result snapshots.
