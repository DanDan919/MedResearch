# AGENTS.md

MedResearch is an AI-assisted scientific evidence synthesis platform for medical and neuroscience research. It must frame output as evidence synthesis, not diagnosis or treatment advice.

## Start Here

Before significant changes, read:

- `AGENTS.md`
- `ARCHITECTURE.md`
- `docs/development/current-state.md`
- existing ADRs in `docs/architecture/decisions/`

## Development Rules

1. Read `AGENTS.md`, `ARCHITECTURE.md`, and `docs/development/current-state.md` before significant changes.
2. Inspect existing code before creating new abstractions.
3. Prefer modifying existing abstractions over creating duplicate ones.
4. Domain logic must not depend on Infrastructure.
5. Application orchestrates use cases.
6. Infrastructure handles external systems and persistence.
7. API handles HTTP concerns.
8. Do not put business rules in controllers.
9. Do not introduce abstractions without a concrete reason.
10. Use async APIs for I/O.
11. Nullable reference types must remain enabled.
12. Avoid `.Result` and `.Wait()`.
13. Avoid magic strings for domain states.
14. Validate external data before allowing it into trusted domain state.
15. LLM output must NEVER be treated as trusted input.
16. Future LLM structured output must be schema validated.
17. Do not send unrelated persisted data to external LLM providers without explicit approval.
18. Missing scientific data must remain missing/null rather than being guessed.
19. Every scientific claim in the future synthesis layer must be traceable to its source.
20. Medical output must be framed as evidence synthesis, not diagnosis or treatment advice.
21. Persisted Evidence must be traceable to a Study and bounded source text.
22. Abstract-level Evidence must not be represented as full-paper evidence.
23. Absence of methodological detail from available source material must never be converted into a negative quality judgment.
24. MedResearch internal evidence evaluation must not be presented as formal GRADE, RoB 2, ROBINS-I, AMSTAR-2, or another validated framework unless that framework is explicitly implemented.
25. Every substantive persisted ResearchReport claim must reference validated Evidence from the same ResearchRun.
26. Scientific insufficiency must be represented explicitly rather than replaced with model prior knowledge.
27. Study/evidence direction counts are descriptive corpus context only, not certainty weights, vote counts, or statistical estimators.
28. PostgreSQL integration behavior must be verified against real PostgreSQL; do not replace database-specific tests with EF Core InMemory.
29. ResearchRun processing leases must not permit stale workers to overwrite a run after ownership has transferred.
30. Normal automated tests must not call live OpenAI, PubMed, or arbitrary internet services; use fake providers or fake HTTP.
31. `Study` identity is global, but `Evidence`, `EvidenceExtraction`, `EvidenceEvaluation`, and report citations must remain scoped to the relevant `ResearchRun`.
32. Scientific search source expansion must preserve per-source and per-search `LiteratureSearch`/`ResearchStudyDiscovery` provenance; do not collapse multiple providers into one search record.
33. Stable `Study` identity uses normalized PMID, PMCID, and DOI only; never merge studies by title, fuzzy metadata, author similarity, or year.
34. Numeric Evidence grounding must bind each retained reported statistic to a unique canonical span of the exact extraction SourceMaterial; numeric token presence alone is insufficient.
35. `Ambiguous` or `Unsupported` numeric grounding must not be promoted to a quantitative synthesis input; LLM output cannot upgrade grounding status.
36. Source anchors use a versioned deterministic normalization and hash; changes to SourceMaterial content must create a new source snapshot rather than reusing an old anchor.

## Development Trail

Record notable bugs, architectural problems, surprising behavior, and failed approaches in `docs/development/problems.md`. Do not record every trivial typo.

Use ADRs for significant architectural decisions. If a decision is replaced, mark the old ADR as superseded instead of rewriting history.

34. SourceMaterial is an immutable scientific content snapshot for extraction; completed EvidenceExtraction must retain its exact SourceMaterialId.

35. EvidenceCorpus is run-scoped and must reject cross-run Evidence or incoherent Evidence -> EvidenceExtraction -> SourceMaterial -> Study lineage before synthesis.

36. Structured full text is a source-coverage scope, not a universal methodological quality score; do not average raw effect values across incompatible studies.37. Quantitative synthesis may only use an explicit deterministic statistical model over eligible compatible evidence; never average raw effect values or let an LLM calculate pooled estimates.
38. LLM output may extract reported statistics from grounded SourceMaterial, but deterministic Application code must perform authoritative statistical normalization.
39. Missing CI confidence level, population, comparator, or study-design compatibility must remain explicit rather than being guessed for a more complete-looking quantitative result.
40. Live validation that calls OpenAI, PubMed, Europe PMC, or structured full-text endpoints must remain explicit opt-in, outside normal solution tests/CI, and must use an acknowledged isolated database.

41. Heterogeneity diagnostics must use the exact same independent Study contributions, analysis-scale effects, and inverse-variance weights as the deterministic fixed-effect synthesis they describe.
42. Cochran's Q and I-squared are statistical diagnostics only; do not present them as quality scores, causal explanations, or automatic random-effects model selectors.
43. I-squared must be bounded at zero and must not be interpreted as proof that true effects are identical when it equals zero.
44. REML tau-squared is between-study variance foundation data only; do not use it to imply HKSJ intervals, prediction intervals, tau-squared confidence intervals, automatic model selection, or replacement of existing Q-derived I-squared unless those methods are explicitly implemented. M22 random-effects weights and pooled estimates must consume the existing M19 REML tau-squared estimate and remain separate from the M17 common/fixed-effect result.
45. HKSJ summary-effect inference is deterministic Application code over the existing M22 random-effects estimate and weights; LLMs must never calculate, modify, select, or infer HKSJ values.
46. Random-effects prediction intervals are deterministic Application code over the existing M22 random-effects estimate, Wald summary variance, and M19 REML tau-squared; LLMs must never calculate, modify, select, or infer prediction intervals.
47. Quantitative synthesis artifacts are immutable run-scoped snapshots. Contribution rows must retain exact numeric values and validated Evidence lineage; retries must be idempotent and conflicting snapshots must not be overwritten.
48. Production stage stores must fence writes with the current ResearchRun lease owner and version; a stale worker must not persist scientific output after lease transfer.
49. Codex CLI is a development/manual-E2E structured LLM adapter only; it must never become the default or production provider, and it must not read or persist Codex authentication credentials.
50. Validation-guided LLM repair may only retry a typed repairable issue within its bounded budget; never weaken the validator or persist the rejected candidate.
51. The Evidence & Provenance read model must project persisted run-scoped lineage; the frontend must not reconstruct citations or expose SourceMaterial.Content.
52. Provenance queries must preserve global Study identity while filtering Evidence, extraction, evaluation, report claims, and quantitative lineage to the requested ResearchRun.
53. Provider index/ingestion dates are not publication dates; incomplete publication metadata must remain incomplete.
54. Successful zero results and provider failure are distinct. Logical provider attempt outcomes must be durable, run-scoped and lease-fenced, without secrets or raw response transcripts.
55. External success bodies must have explicit byte caps and cancellation-aware read deadlines; HTTP 200 is not permission to buffer an unlimited response.
56. Citation existence proves lineage, not entailment; authoritative report claims must have deterministically validated structured semantics and backend-rendered text.
57. Model-authored numeric prose cannot override grounded Evidence or persisted quantitative artifact values; mixed Evidence cannot become a uniform effect, and insufficient Evidence is not evidence of no effect.
