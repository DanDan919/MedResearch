# Technical Debt

## F15.1 Remaining Scientific Limits

- Tuple binding is an intentionally narrow deterministic grammar, not biomedical
  semantic entailment. Unrecognized tables, covariate/analysis scopes, complex
  subgroup context and clinical synonyms may remain ineligible; precision takes
  priority over recall.
- Explicit Evidence timepoint is now persisted and mandatory for quantitative
  compatibility. No missing-timepoint wildcard, time-unit equivalence, or
  follow-up harmonization is implemented.
- Raw ResultSummary no longer enters evaluator/synthesis prompts and the corpus
  rechecks source membership/proof. Final report free text can still introduce
  numbers that its citation/direction validator does not semantically verify.
  Source quotation may include numbers outside the accepted result tuple.
- Bounded numeric proof remains accessible in typed domain/persistence tests,
  not yet added to the public provenance DTO/UI; no SourceMaterial body is exposed.
- Durable provider-attempt provenance, global acquisition attribution, Europe PMC
  date semantics, mobile overflow, dependency advisories, native build, browser
  token flow and live OpenAPI drift are unchanged from the independent audit.

## F8 audit follow-up

- Report rows do not yet persist the quantitative artifact id/fingerprint used during synthesis; the relationship is currently implicit in the same synthesis execution.
- Same-run report citation integrity and SourceMaterial current-version uniqueness are protected by application transactions/advisory locks rather than universal composite constraints or triggers.
- F15's original sentence-level claim was disproved by the independent audit.
  F15.1/ADR-029 implement bounded tuple binding and persist explicit timepoint;
  exact context matching is still not ontology-based semantic equivalence.
- F10 adds the first authentication/authorization boundary, but no external identity-provider tenant is configured in this repository. Production deployment must supply a trusted JWT issuer and audience; token issuance, user lifecycle, collaboration, and ownership transfer remain outside scope.

## Current

- F14 found and fixed CORS middleware ordering for protected browser routes;
  the allow-listed preflight must run before authentication/authorization.
- F14 confirms that provider-failure provenance is still operational logging
  rather than a first-class persisted LiteratureSearch attempt, and that
  SourceMaterial acquisition is global per Study rather than run-attributed.
- F15 confirms numeric fields now require a unique canonical SourceMaterial
  anchor and deterministic local statistical association before quantitative
  use. The verifier does not infer equivalence between differently worded
  outcomes, populations, comparators, or timepoints; those remain explicit
  compatibility and semantic limitations.

- Production migration strategy is not decided. Docker Compose uses config-gated startup migrations for local development only.
- OpenAI planning has no bounded retry policy yet. Configuration failures, authentication failures, timeouts, rate limiting, network failures, malformed structured responses, and validation failures currently move the run through the existing safe failure path.
- OpenAI request pacing/rate limiting is not distributed across multiple API instances.
- Evidence extraction has a bounded validation-guided semantic repair attempt for typed repairable output issues. Provider failures, malformed structured responses, non-repairable validation failures, and a still-invalid replacement use the existing safe failure path.
- Evidence extraction is abstract-level only; full-text retrieval, section-aware extraction, and publisher/PDF source handling are not implemented.
- Evidence evaluation has no bounded retry policy yet. Provider failures, malformed structured responses, validation failures, and unsupported methodological claims currently move the run through the existing safe failure path.
- Evidence evaluation is an internal categorical assessment only. It is not a validated GRADE, RoB 2, ROBINS-I, AMSTAR-2, NOS, or other formal study-quality framework.
- Evidence synthesis has a bounded validation-guided semantic repair attempt for typed repairable output issues. Provider failures, malformed structured responses, cross-run/infrastructure invariants, and a still-invalid replacement use the existing safe failure path.
- Evidence synthesis now receives and persists narrow deterministic common/fixed-effect, REML random-effects Wald, canonical HKSJ, and random-effects prediction interval artifacts for eligible compatible OR/RR/HR groups. It still does not implement modified/ad-hoc HKSJ, tau-squared confidence intervals, forest plots, vote counting, formal evidence certainty grading, semantic outcome harmonization, cohort-overlap detection, or systematic-review/primary-study citation-overlap detection.
- Evidence synthesis currently uses exact normalized outcome names for conflict summaries. This avoids unsafe semantic merging but can miss related outcomes expressed with different wording.
- PubMed and Europe PMC request pacing is conservative and local to one process. There is no distributed rate limiter across multiple API instances.
- PubMed History Server retrieval is deferred while retrieval remains bounded to small direct PMID batches.
- Europe PMC live smoke testing is opt-in and outside normal CI, so normal CI proves deterministic adapter behavior but not current live provider availability.
- F7 quantitative workspace is intentionally limited to the F6 snapshot lineage IDs. Study titles/identifiers and contribution-level confidence intervals are not in that artifact; a future human-readable provenance view needs a separately designed read model rather than frontend joins or per-contribution requests.
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
- SourceMaterial current-version uniqueness is serialized by a PostgreSQL transaction advisory lock in both application writers, but the schema does not yet express a partial unique current-version index for every logical source key; direct SQL outside the writer protocol is not protected.
- Europe PMC full-text availability/failure diagnostics are currently operational logs, not a first-class persisted acquisition-attempt table.
- Evidence numeric fields plus M15 readiness can support the first OR/RR/HR fixed-effect model, but they still do not encode richer arm-level data, multiple effect estimates per finding, or a broad taxonomy sufficient for wider meta-analysis families.
- Statistical synthesis is limited to M17 common/fixed-effect, M22 REML random-effects Wald, M23 canonical HKSJ summary-effect inference, and M24 random-effects prediction intervals for inverse-variance OR/RR/HR groups over M15-compatible evidence. Broader effect families, modified/ad-hoc HKSJ, tau-squared confidence intervals, forest plots, and prediction interval selection/recommendation remain future work. Raw EffectValue averaging remains forbidden.

## Quantitative Evidence Eligibility

- Quantitative readiness is explicit and common/fixed-effect, REML random-effects Wald, canonical HKSJ pooled ratio estimates, and random-effects prediction intervals exist, but formal broad meta-analysis remains future work. There is still no modified/ad-hoc HKSJ, tau-squared confidence interval, forest plot, semantic outcome harmonization, or cohort-overlap detection. The deterministic artifacts are persisted, while semantic interpretation remains a separate concern.
- Compatibility keys are intentionally conservative exact-normalized strings. Semantically equivalent outcomes, populations, or comparators expressed differently may remain separate until a validated harmonization method exists.
- Confidence intervals without an explicit confidence level remain quantitatively ineligible for SE derivation; the system does not assume 95%.
- Regression coefficients, raw proportions, event counts, and group-level continuous statistics are not yet normalized into future quantitative synthesis inputs.

## Live Validation

- The F12 live E2E harness completed one bounded run against a disposable UTF-8 PostgreSQL database using Codex CLI, PubMed, and Europe PMC. The persisted report was `InsufficientEvidence` with no validated Evidence or claims; this is runtime completion, not a claim of scientific completeness.
- Live E2E success validates current external-provider availability for one bounded question, not scientific completeness or general provider uptime. The live harness does not persist a semantic-repair attempt counter, so repair execution is proven by deterministic tests rather than claimed from this run.
- OpenAI transport retry is still not implemented; transient OpenAI/API/network failures use the existing safe failure path. Validation-guided repair is semantic output correction only and is not a transport retry.

## Quantitative Heterogeneity Remaining Work

M18 adds deterministic Cochran's Q, df, and I-squared, but several quantitative synthesis features remain intentionally out of scope:

- modified/ad-hoc HKSJ;
- prediction intervals for unsupported effect families and prediction interval selection/recommendation;
- Q p-value calculation;
- uncertainty intervals for I-squared;
- forest/funnel plots and publication-bias diagnostics;
- quantitative UI, forest plots, and broader report composition over the persisted quantitative artifact;
- nuanced interpretation guidance beyond numeric diagnostics and limitations.
- tau-squared confidence intervals, modified/ad-hoc HKSJ, prediction interval selection/recommendation, prediction intervals for unsupported effect families, and tau-based I-squared replacement are intentionally deferred.

## F9 Recovery and CI Follow-up

- Planning and successful search execution retries are now idempotent for the current run/plan contract. Failed provider attempts in a partial multi-source search are still operationally logged rather than persisted as first-class LiteratureSearch status records.
- Local Docker unavailability still prevents execution of PostgreSQL/Testcontainers tests. The GitHub Actions workflow remains the authoritative runtime check and now includes the deterministic Playwright browser suite.
- F10 authentication is intentionally bearer-based and stateless. There is no refresh-token/session lifecycle, localStorage token persistence, cookie auth, or CSRF workflow until a concrete identity-provider/client product is selected.
- Resource creation has no per-user quota or rate limit yet. Authentication prevents cross-user access, but one authenticated actor can still submit many expensive research runs; a narrow resource-abuse policy is a future operational milestone.
- Existing pre-ownership rows are preserved under `legacy-unowned`. A deliberate administrative migration/ownership assignment tool is still required before those rows can be made user-visible.

## F13 Provenance observability limitations

F15.2 closes the failed literature-search provenance gap using lease-fenced `LiteratureProviderAttempt` history and the owner-scoped provenance projection. LiteratureSearch remains successful scientific provenance. Historical searches have no reconstructed attempt history; crashed Started calls remain unknown, and external HTTP execution is not exactly once. Attempt-history retention/pagination is not yet implemented.

SourceMaterial acquisition still logs unavailable vs failed retrieval but does not persist a run-specific acquisition outcome history. Reusing a query/plan search attempt would misrepresent acquisition identity and legitimate full-text unavailable semantics; a future focused model is needed. Global SourceMaterial attribution remains an independent audit issue. Historical publication/index date mistakes are not automatically repaired without source provenance. Provider quotas remain process-local, not distributed across replicas. Successful bodies are now size/deadline bounded; this does not establish scientific completeness, external service correctness or narrative report entailment.

SourceMaterial is global per Study, while acquisition attempts are not separately run-scoped. F13 can show the exact SourceMaterialId used by an extraction and all persisted snapshot metadata for the discovered Study, but cannot claim that every global snapshot was acquired during the selected run. Raw source content remains intentionally outside the read model.
