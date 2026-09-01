# Technical Debt

## Current

- Production migration strategy is not decided. Docker Compose uses config-gated startup migrations for local development only.
- Claimed research runs do not have a lease or automatic recovery path yet. If a process crashes after moving a run from `Queued` to an in-progress status, the run can remain in that state until an operator or future recovery workflow handles it.
- OpenAI planning has no bounded retry policy yet. Configuration failures, authentication failures, timeouts, rate limiting, network failures, malformed structured responses, and validation failures currently move the run through the existing safe failure path.
- OpenAI request pacing/rate limiting is not distributed across multiple API instances.
- Evidence extraction has no bounded retry policy yet. Provider failures, malformed structured responses, validation failures, and grounding failures currently move the run through the existing safe failure path.
- Evidence extraction is abstract-level only; full-text retrieval, section-aware extraction, and publisher/PDF source handling are not implemented.
- Evidence evaluation has no bounded retry policy yet. Provider failures, malformed structured responses, validation failures, and unsupported methodological claims currently move the run through the existing safe failure path.
- Evidence evaluation is an internal categorical assessment only. It is not a validated GRADE, RoB 2, ROBINS-I, AMSTAR-2, NOS, or other formal study-quality framework.
- Evidence synthesis has no bounded retry policy yet. Provider failures, malformed structured responses, validation failures, and unsupported claims currently move the run through the existing safe failure path.
- Evidence synthesis is qualitative only. It does not perform meta-analysis, pooled effect estimation, vote counting, formal evidence certainty grading, semantic outcome harmonization, cohort-overlap detection, or systematic-review/primary-study citation-overlap detection.
- Evidence synthesis currently uses exact normalized outcome names for conflict summaries. This avoids unsafe semantic merging but can miss related outcomes expressed with different wording.
- PubMed retrieval has no bounded retry policy yet. Network failures, timeouts, rate limiting, invalid upstream responses, and parsing failures currently move the run through the existing safe failure path.
- PubMed request pacing is conservative but local to one process. There is no distributed rate limiter across multiple API instances.
- `ResearchPlannerPrompt`, `EvidenceExtractorPrompt`, `EvidenceEvaluationPrompt`, and `ResearchSynthesisPrompt` are versioned but still embedded in code. Move prompts to a resource/template mechanism when prompt review, localization, or runtime prompt experiments become real needs.

## Watch List

- Design a PostgreSQL-backed recovery strategy for stale in-progress `ResearchRun` rows before adding more long-running LLM calls.
- Decide whether the hosted background worker should become a separate `MedResearch.Worker` executable once independent deployment, scaling, or operational lifecycle needs are demonstrated.
- Add additional literature source adapters only when they actually work and are covered by fixtures/tests.
- Add additional LLM providers only when a real provider is selected and can be tested behind `IStructuredLlmClient`.
- Decide whether `/health` should become a richer operational health check when more external dependencies exist.
- Run PostgreSQL Testcontainers integration tests in an environment where Docker Desktop is running.
- Consider an explicit opt-in live OpenAI smoke test only if the development workflow needs it.
- Review whether evidence evaluation and synthesis should persist provider-attempt diagnostics separately from terminal run failures before adding retries or batch reprocessing.
