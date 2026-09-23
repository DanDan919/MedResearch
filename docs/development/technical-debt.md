# Technical Debt

## Current

- Production migration strategy is not decided. Docker Compose uses config-gated startup migrations for local development only.
- OpenAI planning has no bounded retry policy yet. Configuration failures, authentication failures, timeouts, rate limiting, network failures, malformed structured responses, and validation failures currently move the run through the existing safe failure path.
- OpenAI request pacing/rate limiting is not distributed across multiple API instances.
- Evidence extraction has no bounded retry policy yet. Provider failures, malformed structured responses, validation failures, and grounding failures currently move the run through the existing safe failure path.
- Evidence extraction is abstract-level only; full-text retrieval, section-aware extraction, and publisher/PDF source handling are not implemented.
- Evidence evaluation has no bounded retry policy yet. Provider failures, malformed structured responses, validation failures, and unsupported methodological claims currently move the run through the existing safe failure path.
- Evidence evaluation is an internal categorical assessment only. It is not a validated GRADE, RoB 2, ROBINS-I, AMSTAR-2, NOS, or other formal study-quality framework.
- Evidence synthesis has no bounded retry policy yet. Provider failures, malformed structured responses, validation failures, and unsupported claims currently move the run through the existing safe failure path.
- Evidence synthesis now receives narrow deterministic common/fixed-effect and REML random-effects Wald pooled results for eligible compatible OR/RR/HR groups, but it does not persist quantitative result snapshots or implement HKSJ, prediction intervals, tau-squared confidence intervals, forest plots, vote counting, formal evidence certainty grading, semantic outcome harmonization, cohort-overlap detection, or systematic-review/primary-study citation-overlap detection.
- Evidence synthesis currently uses exact normalized outcome names for conflict summaries. This avoids unsafe semantic merging but can miss related outcomes expressed with different wording.
- PubMed and Europe PMC request pacing is conservative and local to one process. There is no distributed rate limiter across multiple API instances.
- PubMed History Server retrieval is deferred while retrieval remains bounded to small direct PMID batches.
- Europe PMC live smoke testing is opt-in and outside normal CI, so normal CI proves deterministic adapter behavior but not current live provider availability.
- Europe PMC provider-record identity is retained as provenance but is not yet modeled as a first-class unique identifier for records that lack PMID, PMCID, and DOI.
- `ResearchPlannerPrompt`, `EvidenceExtractorPrompt`, `EvidenceEvaluationPrompt`, and `ResearchSynthesisPrompt` are versioned but still embedded in code. Move prompts to a resource/template mechanism when prompt review, localization, or runtime prompt experiments become real needs.
- Study identity normalization is intentionally conservative. PMID, PMCID, and normalized DOI unique indexes deduplicate reported identifiers, but provider-record-only identity, conflicting stable identifier graphs, and studies without stable identifiers are not semantically merged.
- The report claim/evidence join table enforces citation existence with FKs, while the same-ResearchRun citation invariant is enforced by Application validation and integration tests. A pure PostgreSQL constraint would require redundant run ids, triggers, or a different citation table shape.

## Watch List

- Monitor lease duration and heartbeat defaults under real CI/runtime load; tune them before adding longer-running providers or full-text stages.
- Decide whether the hosted background worker should become a separate `MedResearch.Worker` executable once independent deployment, scaling, or operational lifecycle needs are demonstrated.
- Add any third or later literature source adapter only when it actually works and is covered by fixtures/tests.
- Add additional LLM providers only when a real provider is selected and can be tested behind `IStructuredLlmClient`.
- Decide whether health output should expose richer machine-readable readiness details when more external dependencies exist.
- Keep CI as the authoritative PostgreSQL/Testcontainers runtime check while local Docker Desktop remains unavailable.
- Consider an explicit opt-in live OpenAI smoke test only if the development workflow needs it.
- Review whether evidence evaluation and synthesis should persist provider-attempt diagnostics separately from terminal run failures before adding retries or batch reprocessing.

- EvidenceCorpus is an application read model over persisted rows rather than a versioned database snapshot. Reproducibility depends on immutable SourceMaterial and extraction references; a future audit/export requirement may justify persisting a corpus manifest.
- SourceMaterial current-version uniqueness is protected by application/advisory-lock behavior and PostgreSQL identity indexes, but the schema does not yet express a partial unique current-version index for every logical source key.
- Europe PMC full-text availability/failure diagnostics are currently operational logs, not a first-class persisted acquisition-attempt table.
- Evidence numeric fields plus M15 readiness can support the first OR/RR/HR fixed-effect model, but they still do not encode richer arm-level data, multiple effect estimates per finding, or a broad taxonomy sufficient for wider meta-analysis families.
- Statistical synthesis is limited to M17 common/fixed-effect and M22 REML random-effects Wald inverse-variance OR/RR/HR groups over M15-compatible evidence. Broader effect families, HKSJ, prediction intervals, tau-squared confidence intervals, forest plots, and persisted quantitative result snapshots remain future work. Raw EffectValue averaging remains forbidden.

## Quantitative Evidence Eligibility

- Quantitative readiness is explicit and common/fixed-effect plus REML random-effects Wald pooled ratio estimates exist, but formal broad meta-analysis remains future work. There is still no HKSJ inference, prediction interval, tau-squared confidence interval, forest plot, semantic outcome harmonization, persisted quantitative result artifact, or cohort-overlap detection. Q/df/I-squared diagnostics are transient read-model metadata.
- Compatibility keys are intentionally conservative exact-normalized strings. Semantically equivalent outcomes, populations, or comparators expressed differently may remain separate until a validated harmonization method exists.
- Confidence intervals without an explicit confidence level remain quantitatively ineligible for SE derivation; the system does not assume 95%.
- Regression coefficients, raw proportions, event counts, and group-level continuous statistics are not yet normalized into future quantitative synthesis inputs.

## Live Validation

- The live E2E harness exists but was not executed in this environment because no live OpenAI key and isolated PostgreSQL runtime were configured. It should be run only against a disposable database with explicit `MEDRESEARCH_LIVE_E2E_DATABASE_ACK=isolated`.
- Live E2E success will validate current external-provider availability for one bounded question, not scientific completeness or general provider uptime.
- OpenAI structured generation still has no bounded retry policy; live failures from transient OpenAI/API/network issues use the existing safe failure path.

## Quantitative Heterogeneity Remaining Work

M18 adds deterministic Cochran's Q, df, and I-squared, but several quantitative synthesis features remain intentionally out of scope:

- HKSJ inference and modified/ad-hoc HKSJ;
- prediction intervals;
- Q p-value calculation;
- uncertainty intervals for I-squared;
- forest/funnel plots and publication-bias diagnostics;
- persisted quantitative result artifacts or report API DTOs dedicated to quantitative synthesis;
- nuanced interpretation guidance beyond numeric diagnostics and limitations.
- tau-squared confidence intervals, HKSJ, prediction intervals, and tau-based I-squared replacement are intentionally deferred.
