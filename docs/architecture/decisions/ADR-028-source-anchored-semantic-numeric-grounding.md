# ADR-028: Source-Anchored Semantic Numeric Grounding

## Status

Superseded by ADR-029 (2026-10-08).

The independent 2026-10-07 audit disproved the original sentence-level
Frankenstein-protection claim below. The original decision text is retained as
historical evidence; ADR-029 describes the corrected implemented boundary.

## Context

The earlier extraction boundary proved that `supportingText` appeared in the
selected `SourceMaterial`, and that reported numeric tokens appeared somewhere
in that text. That lexical guarantee did not prove that an effect estimate,
confidence interval, p-value, or sample size belonged to the same statistical
context. A model could therefore assemble a plausible-looking finding from
different outcomes, subgroups, or sentences.

## Decision

Evidence extraction uses a versioned `SourceAnchor` resolved against the exact
`SourceMaterial` used by the extraction. Version `source-text-v1` applies the
existing FormKC, whitespace-collapse, trim, and lower-case normalization. The
anchor stores the canonical start/end offsets, normalized span text, source
material id, and SHA-256 span hash. A supporting excerpt must occur exactly
once; zero matches are unsupported and repeated matches are ambiguous.

`SemanticNumericGroundingVerifier` evaluates the bounded anchor by deterministic
local sentence rules. It requires effect measures and estimates to co-occur,
requires CI bounds in the same CI context (and with the effect value when one
is reported), requires a p-value operator/value in the same effect context, and
rejects conservatively scoped intervention/control/arm/subgroup sample sizes.
The p-value operator is taken from the source; a conflicting operator from the
LLM invalidates that p-value fact. Each field carries `Verified`, `Ambiguous`,
or `Unsupported`; fields not supplied remain not applicable rather than being
invented.

The facts and optional p-value operator are persisted on `Evidence` as JSONB,
with a forward EF migration. Quantitative eligibility requires `Verified`
grounding for the numeric fields it uses. The LLM never calculates or upgrades
grounding status. F12 validation-guided repair remains bounded and re-enters
the same validator, so a repair cannot bypass these checks.

## Consequences

This blocks the main Frankenstein-number failure mode without changing M17-M24
statistical formulas or allowing raw text to become a quantitative estimate.
Offsets are canonical normalized offsets, not offsets into the unnormalized
provider payload. Local matching is intentionally conservative and is not a
clinical ontology, semantic entailment system, or complete timepoint model.
Outcome/population/comparator compatibility still needs the existing
quantitative compatibility rules; timepoint-specific extraction is a future
decision. Legacy persisted Evidence with an empty grounding collection is not
silently considered source-verified.

## Verification

Application tests cover unique/repeated anchors, complete OR/CI/p-value
contexts, Frankenstein cross-sentence values, wrong effect measures, scoped
sample sizes, exact p-value operators, and conflicting LLM operators.
Infrastructure tests cover JSONB persistence round-trip. Quantitative tests
cover the persisted `Verified` gate. PostgreSQL tests remain Testcontainers
tests and are authoritative in CI when local Docker is unavailable.
