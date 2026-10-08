# ADR-029: Bound Statistical Tuples and Trusted Synthesis Inputs

Status: Accepted
Date: 2026-10-08
Supersedes: ADR-028

## Context

The independent audit at `0de619c` reproduced false `Verified` relationships:
another result's interval/SE/confidence level, wrong sign, ordinary `or`, and
hospital count. Compatibility omitted intervention. Rejected numbers also
survived in raw LLM `ResultSummary`. Green tests did not prove these predicates.

## Decision

Keep exact immutable SourceMaterial anchors and `source-text-v1` normalization.
Resolve overlapping occurrences conservatively. A bounded deterministic binder
selects exactly one explicit measure/signed-value expression with a preceding
outcome label. It does not choose nearest/first results. Explicit contrast clauses
may separate results; multiple expressions in an unseparated clause or multiple
plausible results are ambiguous. CI/level, SE, and p/operator must be attached
through a restricted statistical-expression grammar, not arbitrary prose.

Anchors additionally retain a case-preserving `LexicalText` projection derived
from the exact source, not the model's quotation. Its lowercase characters must
equal canonical Text, and downstream source membership authenticates its case.
This additive JSON proof does not change historical canonical offsets/hashes.
Bare `OR` requires source-case proof; lowercase English `or` cannot qualify.
Legacy anchors without lexical proof may support full `odds ratio` labels, but
cannot prove bare OR. No historical anchors are rewritten or silently upgraded.

Quantitative context requires an explicit normalized intervention/comparator
pair (`versus`, `vs`, `compared with/to`, `against`), population/outcome context,
and a reported outcome timepoint (`at`, `after`, or labelled follow-up). Timepoint
is persisted as nullable bounded Evidence text. Missing intervention or timepoint
is not a wildcard, including when both are absent. No clinical synonym matching
or unit/time equivalence is inferred. Multiple sample scopes are ambiguous;
participant roles with explicit enrollment/randomization/analysis/overall scope
are distinct from hospital counts and arm/subgroup counts.
An explicit n= still requires a participant role; a following nonparticipant
unit cannot qualify just because the clause mentions randomization. One numeric
occurrence represented as both n= and a participant count is counted once.

The internal synthesis corpus loads authoritative content only for exact
extraction source IDs. Source identity, known normalization, immutable content
hash, unique membership, canonical offsets, span hash, Study/run/extraction/scope
lineage and current tuple predicates are rechecked. A self-consistent hash is not
membership proof. Required persisted facts must exist uniquely and remain
Verified. Legacy proof is not upgraded. CI-derived variance additionally requires
confidence-level proof. Domain rejects undefined direction and Verified without
anchor. These are Application/domain safeguards, not universal SQL triggers.

`SynthesisContextBuilder` consumes the revalidated corpus projection, never its
raw input snapshot. Unverified numeric fields are nulled; raw ResultSummary is
excluded from both synthesis and evaluation prompts. Synthesis formats grounded
structured fields and an explicitly labelled source quotation. Raw extraction
descriptions remain persisted for provenance but are not scientific authority.

The compatibility key uses a versioned `estimand-v2` prefix and length-prefixed
outcome/population/comparator/design/measure/intervention/timepoint. Existing
artifacts remain immutable historical snapshots. M17-M24 calculations and
reference values for the same valid contributions are unchanged.

## Consequences and Limits

Verified means unique deterministic role/relation binding under this implemented
grammar and exact source membership, not biomedical truth, causality, validated
methodological quality, or universal semantic entailment. Precision is favored
over recall: paraphrases, tables, complex covariate/subgroup/analysis descriptions,
unlabelled timepoints and partial expressions may be Unsupported/Ambiguous.

The quote may contain other source numbers; it is not permission to import them
into a claim. Final free-text report validation does not prove every numeric
assertion. Neither this decision nor prompt instructions solve general narrative
hallucination. Existing bounded F12 repair re-enters the same validator/source;
unsupported optional numeric fields are conservatively dropped rather than
making the model manufacture missing information.

## Verification

Permanent adversarial tests cover the audit counterexamples, positive compatible
controls, role reversal, timepoint mismatch/missingness, overlapping anchors,
corrupt proof/membership, confidence-level gating, actual provider prompt input,
and fresh PostgreSQL readback. Real PostgreSQL migration/runtime tests remain
mandatory in CI; local unavailable-Docker skips are not runtime verification.
