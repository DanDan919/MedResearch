# ADR-022: Persisted Quantitative Synthesis Artifacts

Date: 2026-09-29

## Status

Accepted.

## Context

M17-M24 produced deterministic quantitative read-model values in Application: fixed-effect inverse-variance synthesis, Q/df/I-squared, REML tau-squared, random-effects Wald synthesis, canonical HKSJ inference, and a random-effects prediction interval. Those values were supplied to the synthesis prompt but disappeared after the stage completed. Recomputing them from mutable operational rows also made an audit or client read model dependent on repeating the calculation.

The result must remain separate from the narrative LLM output. It must preserve the exact current-run contribution set, including both fixed-effect and random-effects weights, and remain safe under stage retry or concurrent workers.

## Decision

Persist one `quantitative_synthesis_artifacts` row per `(ResearchRunId, GroupKey)`. The row stores a deterministic JSON snapshot of the complete `QuantitativeSynthesisResult`, including successful and `NotSynthesizable` states, tau-squared availability, Wald/HKSJ/prediction outputs, failure reasons, and algorithm versions. `double` values remain PostgreSQL `double precision` in contribution snapshots; no decimal conversion is used for statistical values.

Persist exact contribution rows in `quantitative_synthesis_contribution_snapshots`. Each row stores the Evidence, Study, EvidenceExtraction, SourceMaterial, model (`fixed-effect` or `random-effects`), ordinal, analysis-scale effect/variance/SE, weight, and normalized weight. Foreign keys plus application lineage checks ensure every contribution belongs to the analyzed ResearchRun and its Study.

The artifact store writes the artifact set and contribution rows in one transaction. A unique `(ResearchRunId, GroupKey)` index provides idempotency. A retry with the same fingerprint is a no-op; a different fingerprint is a conflict and cannot overwrite the original snapshot. The artifact is persisted by `SynthesisContextBuilder` before the narrative LLM call, and the same in-memory deterministic result is passed to `SynthesisContext`; the LLM is never the source of quantitative values.

Expose artifacts through `GET /api/research/{researchRunId}/quantitative`. The endpoint returns deterministic outputs and lineage identifiers only. It does not return raw SourceMaterial text, prompts, provider secrets, or worker lease data. No quantitative frontend workspace is introduced in this milestone.

## Consequences

- Completed pipeline stages have durable, machine-readable quantitative evidence of what was calculated.
- Reproducibility can be checked using the snapshot fingerprint and exact contribution rows.
- Non-estimated states remain distinct from zero estimates because the full result preserves nullable fields and status/reason codes.
- Quantitative artifacts may exist before a narrative report if the later LLM/report write fails; the next retry reuses the immutable artifact and cannot silently change it.
- Future schema evolution requires an explicit snapshot/version decision rather than mutating historical results.

## Rejected Alternatives

- Recalculating on every API read: rejected because it is not an immutable audit artifact and could observe changed operational inputs.
- Storing only an unchecked UUID array: rejected because it cannot prove Evidence lineage or preserve exact weights.
- Asking the LLM to recreate or summarize numeric values: rejected because statistical calculations remain a deterministic Application responsibility.
- Adding a quantitative UI: deferred to the next milestone; F6 provides only the read contract needed by clients.
