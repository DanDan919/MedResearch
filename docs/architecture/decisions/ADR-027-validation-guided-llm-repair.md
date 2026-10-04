# ADR-027: Validation-guided bounded LLM repair

## Status

Accepted

## Context

Strict JSON Schema proves structural shape, but it cannot prove scientific
grounding or that a report claim is compatible with the supplied Evidence. F11
demonstrated this with real Codex CLI output: one run was rejected because an
extraction excerpt was absent from the trusted abstract, and another reached
synthesis but was rejected for an unsupported mixed/conflict claim.

Sending an invalid candidate directly to persistence would weaken the trust
boundary. Retrying transport is also a different concern from asking a model to
replace a semantically invalid object.

## Decision

Application owns `ValidationGuidedLlmRepairService`. Validators expose typed
`ValidationIssue` values with a stable code, optional contract path, bounded
repair instruction, and `Repairable` or `NonRepairable` disposition.

Evidence extraction and research synthesis may make a complete replacement
request for repairable issues. The replacement preserves the original prompt
context and output schema, is validated from scratch by the same validator, and
is the only candidate that can leave the stage. The default semantic repair
budget is one attempt and configuration is bounded to zero through two.

Cross-run context defects, lease/ownership problems, cancellation, provider and
transport failures, malformed infrastructure state, and other non-repairable
issues fail closed without another LLM call. Planner and methodological
evaluation semantic repair remain disabled until their own contracts justify it.

The service does not merge candidates, apply JSON patches, relax validators, or
persist diagnostic payloads. It logs only bounded stage and issue-code metadata.
Invalid output therefore cannot become Evidence, Evaluation, or a ResearchReport.

## Consequences

- A provider can recover from a small, well-classified semantic mistake without
  changing scientific validators.
- The same trusted SourceMaterial or SynthesisContext remains authoritative.
- A bounded second failure is visible through the existing safe run failure path.
- Semantic repair does not solve numeric grounding gaps: numeric token presence
  is still weaker than semantic association between a number and a statistic.
- Transport retries, provider reliability, and live external availability remain
  separate operational concerns.
