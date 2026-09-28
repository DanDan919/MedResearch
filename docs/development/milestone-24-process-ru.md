# Milestone 24 Process Log: random-effects prediction interval foundation

## Стартовое состояние

- Репозиторий: `E:\MedResearch`
- Branch: `main`
- Actual starting HEAD: `52cecc075739c63e472ea882b2b45dda1d7edb0a`
- Remote: `https://github.com/DanDan919/MedResearch.git`
- Working tree перед изменениями: clean
- `CODEX_CONTEXT.md` в репозитории отсутствовал; работа продолжалась по `AGENTS.md`, audit document, ADR-019 и фактическому коду.

## Прочитанный контекст

- `docs/development/current-architecture-hallucination-audit-ru.md`
- `AGENTS.md`
- `docs/architecture/decisions/ADR-019-random-effects-reml-wald-quantitative-synthesis.md`
- M22 implementation: `RandomEffectsQuantitativeStatisticalSynthesizer`
- M23 implementation: `HksjSummaryEffectInferenceCalculator`
- `StudentTQuantile`
- `QuantitativeSynthesisContracts`
- `SynthesisContextContracts`
- `SynthesisContextBuilder`
- `ResearchSynthesisPrompt`
- quantitative Application tests
- synthesis context tests
- full fake pipeline integration test assertions

## Методологическая проверка

Проверены текущие authoritative sources:

- Cochrane Handbook, Chapter 10, section 10.10.4.3, prediction intervals from random-effects meta-analysis.
- RevMan documentation pointing prediction interval interpretation/settings back to Cochrane Handbook section 10.10.4.3.
- `metafor` documentation for `predict.rma` and `rma.uni`.

Выбран узкий V1-метод:

```text
df = k - 1
prediction_variance = Var(theta_RE) + tau²
prediction_SE = sqrt(prediction_variance)
PI = theta_RE +/- t_(1-alpha/2, df) * prediction_SE
```

Где:

- `theta_RE` берется из M22 random-effects result;
- `Var(theta_RE)` берется из M22 Wald summary variance;
- `tau²` берется из M19 REML estimate, already consumed by M22;
- confidence level берется из `QuantitativeSynthesis:OutputConfidenceLevel`;
- OR/RR/HR рассчитываются на log-scale, а reported scale получается через `exp(...)` после расчета analysis-scale endpoints.

Это соответствует current Cochrane simple random-effects prediction interval и согласуется с `metafor` `test="t"` convention for `p = 1`, то есть `df = k - 1`.

## Явно не реализовано

- modified/ad-hoc HKSJ;
- prediction interval selection/recommendation;
- alternative Riley-style / k-2 variants;
- tau-squared confidence interval;
- prediction intervals for unsupported effect families;
- p-values;
- persisted quantitative artifacts;
- automatic inference/model selection.

## Архитектурное решение

Добавлен `RandomEffectsPredictionIntervalCalculator` в Application.

Он:

- принимает только готовый M22 `QuantitativeRandomEffectsSynthesisResult`;
- не оценивает tau² повторно;
- не меняет M17 fixed/common-effect result;
- не меняет M18 Q/df/I²;
- не меняет M22 Wald values;
- не меняет M23 HKSJ values;
- возвращает explicit `QuantitativePredictionIntervalResult` рядом с `HksjInference`.

`SynthesisContext` получил `SynthesisPredictionIntervalContext`.

`ResearchSynthesisPrompt` теперь:

- включает `PredictionInterval` рядом с Wald/HKSJ;
- запрещает LLM рассчитывать, изменять, выбирать или выводить prediction interval самостоятельно.

Схема БД не менялась.

## Boundary semantics

- `k = 1`: unavailable, `df = 0`.
- `k = 2`: calculable, `df = 1`, Student-t critical is intentionally wide.
- `k >= 3`: calculable if all values finite.
- `tau² = 0`: valid; prediction variance collapses to M22 summary variance, но interval uses Student-t critical value and does not replace the existing z-Wald CI.
- non-finite tau²/effect/variance/endpoints/back-transform: explicit `NotSynthesizable`.

## Тесты

Добавлены/усилены тесты для:

- BCG/metafor reference values;
- `df = k - 1`;
- `k = 1`;
- `k = 2`;
- `k = 3` / `tau² = 0`;
- positive tau² via BCG reference;
- order independence;
- non-finite inputs;
- ratio-scale back-transform through BCG RR reference;
- preservation of M22 Wald and M23 HKSJ values;
- `SynthesisContext` projection;
- prompt propagation and LLM no-calculation boundary;
- full fake pipeline prompt/context assertions.

Локальный `Rscript` не установлен, поэтому локальный runtime verification against `metafor` не выполнялся. Reference expectations were encoded from documented `metafor` BCG workflow and Cochrane/metafor formulas, not from a live statistical-service dependency.

## Validation results

- `dotnet restore MedResearch.slnx`: passed.
- `dotnet build MedResearch.slnx --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet test MedResearch.slnx --no-build`: passed locally.
  - Domain: 25 passed, 0 failed, 0 skipped.
  - Application: 151 passed, 0 failed, 0 skipped.
  - Infrastructure: 65 passed, 0 failed, 0 skipped.
  - Integration: 9 passed, 0 failed, 62 skipped.
  - Local PostgreSQL/Testcontainers skips are expected on this machine because Docker Desktop Linux engine is unavailable.
- Targeted Application quantitative/synthesis tests: 45 passed, 0 failed, 0 skipped.
- Targeted full fake pipeline integration test: skipped locally because Docker/Testcontainers unavailable.
- `dotnet ef migrations has-pending-model-changes --project src/MedResearch.Infrastructure/MedResearch.Infrastructure.csproj --startup-project src/MedResearch.Api/MedResearch.Api.csproj`: passed, no pending model changes.
- `docker compose config`: passed.
- `git diff --check`: passed; Git reported line-ending normalization warnings only.
- `docker info`: failed locally because Docker daemon pipe `dockerDesktopLinuxEngine` was unavailable.
- Security keyword scan found only placeholders/development passwords/test keys and existing documentation/test references; no real secret was introduced.

## CI expectations

Normal CI should run the deterministic Application tests, normal fake-provider tests, and Docker-backed PostgreSQL/Testcontainers suite with `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true`. This milestone does not require live OpenAI, live PubMed, Europe PMC, or external statistical services.

## CI result

- Workflow: `.github/workflows/ci.yml`
- Run: `36404421685`
- URL: `https://github.com/DanDan919/MedResearch/actions/runs/36404421685`
- Commit: `223392e1cfab456b0d33b7a66d2bd512ba9c07ff`
- Runner: `ubuntu-latest`
- .NET: `10.0.x`
- Status: success
- Job: `Build and test`, success
- CI `Docker info`: success
- CI `Test`: success
- CI EF pending-model check: success
- CI Docker Compose config: success
- Workflow sets `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true`; its TRX parser fails the job if any test reports skipped tests. Because the run succeeded, required Docker/PostgreSQL/Testcontainers tests did not silently skip.
- Test result artifact `test-results` was uploaded. Public API exposed artifact metadata, but direct artifact/log download from this environment required authentication/admin access, so exact CI TRX counters were not locally extractable here.
