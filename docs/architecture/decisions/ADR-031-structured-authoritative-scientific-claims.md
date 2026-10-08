# ADR-031: Authoritative Scientific Claims Are Structured, Not Free-Text

Date: 2026-10-08
Status: Accepted

## Context

Citation UUID validation establishes lineage, not entailment. Ten pre-fix negative tests accepted semantically incorrect model sentences despite valid Evidence IDs. F15.1 numeric tuple/source protections and immutable quantitative artifacts do not by themselves constrain report wording.

## Decision

LLM synthesis proposes closed structured claim kinds, directions, exact scope labels and authoritative references. It does not author the primary scientific sentence or supply numeric values. Application validates every cited current-run finding, rejects unsupported scope changes and uniform claims over differing directions, and distinguishes insufficiency from no effect.

Study numeric claims select reverified grounded Evidence. Pooled/diagnostic claims select actual persisted artifact IDs and all-and-only contribution Evidence IDs. Backend copies original decimal/double values and method/interval semantics and renders deterministic text. No automatic inference/model choice, new statistics or free-text entailment engine is introduced.

F12 repair reuses the same trusted context, typed issues and schema with one bounded replacement; failed repair cannot weaken validation. Persistence reconstructs support and revalidates before a short fenced transaction; no external I/O occurs within that transaction. Read guards preserve structured/citation/artifact coherence. JSONB semantics and optional FK columns supplement, not replace, same-run application checks.

Historical claims remain LegacyUnverified. Migration does not synthesize scope or numeric proof for old prose. APIs/UI expose authority status and support; raw SourceMaterial content remains absent. Optional old extracted summaries are visibly unverified and cannot override the structured core.

## Consequences

Precision is preferred over semantic recall: scope comparison is deterministic case/whitespace normalization, not synonym matching or population generalization. Direction is the persisted Evidence category, not independently verified biomedical benefit or causality. Insufficient Evidence cannot become proof of no effect. Rendering avoids causal, clinical significance and certainty upgrades.

This establishes only that authoritative structured claims satisfy implemented support rules against declared Evidence/artifacts. It does not solve arbitrary hallucination, observational-study interpretation, provider correctness or general natural-language entailment. M17-M24 calculations and F15.1/F15.2 guarantees remain separate and unchanged.
