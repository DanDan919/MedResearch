# ADR-014: Quantitative Evidence Eligibility Before Statistical Pooling

Date: 2026-09-16

## Status

Accepted

## Context

MedResearch can now build a traceable, run-scoped EvidenceCorpus. Some Evidence rows contain numeric fields such as effect measure, effect value, confidence interval bounds, p-value, and sample size. Numeric values alone are not enough for scientific pooling. Odds ratios, risk ratios, mean differences, standardized mean differences, correlations, and p-values have different semantics and cannot be averaged together.

The system also needs to preserve the trust boundary established by SourceMaterial and EvidenceCorpus. LLM output is allowed to extract reported statistics only when grounded in the exact source snapshot. It must not perform authoritative statistical transformations or fill missing uncertainty.

## Decision

Add a deterministic Application read-model layer named `QuantitativeEvidenceAssessor` after EvidenceCorpus construction. It produces `QuantitativeEvidenceReadiness`, per-Evidence `QuantitativeEvidenceAssessment` records, and `CompatibleEvidenceGroup` records for future statistical synthesis input checks.

The layer is not persisted. It is derived from persisted Evidence, EvidenceExtraction, SourceMaterial, Study, and evaluation/search lineage already validated by EvidenceCorpus. Evidence persistence is extended only for source-reported `ConfidenceLevel` and `ReportedStandardError`, because without those raw fields deterministic uncertainty derivation would either be impossible or would require unsafe assumptions.

The assessor:

- classifies reported effect-measure labels into explicit `EffectMeasureType` values;
- preserves reported statistics separately from normalized/derived statistics;
- derives log ratio effects, CI/SE-based variance, and Fisher z correlations in deterministic C#;
- records `StatisticOrigin` for reported vs derived uncertainty;
- emits explicit `QuantitativeIneligibilityReason` values;
- groups only by conservative normalized outcome, population, comparator, study design, and effect-measure type;
- tracks `UniqueStudyCount` and marks groups with multiple Evidence from one Study as dependent rather than independent.

## Consequences

MedResearch can now say which Evidence is ready as input to a future quantitative synthesis engine, and why other Evidence is not. It still does not implement pooled effects, fixed/random effects, heterogeneity statistics, forest plots, vote counting, or meta-analysis claims.

The design intentionally favors false-negative eligibility over unsafe false-positive pooling. Semantically equivalent outcomes or populations with different wording may remain separate until a validated harmonization method exists.

## Rejected Alternatives

- Persisting quantitative assessments immediately: rejected because the assessments are deterministic over current persisted lineage and do not yet need snapshot storage.
- Assuming 95% confidence intervals by convention: rejected because missing confidence level must remain missing.
- Asking the LLM to calculate log effects, SE, or variance: rejected because statistical normalization must be deterministic and testable.
- Grouping by fuzzy outcome or title similarity: rejected because this would manufacture scientific compatibility.