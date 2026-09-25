# Milestone 23 Process Log: canonical HKSJ summary-effect inference

Дата: 2026-09-25

## Стартовое состояние

- Репозиторий: `E:\MedResearch`
- Ветка: `main`
- Remote: `https://github.com/DanDan919/MedResearch.git`
- Фактический стартовый HEAD: `902ad31d555dd2241c1b8b6a5d8faf0cd27b13f7`
- Рабочее дерево перед изменениями: чистое
- Базовый успешный CI: `35853113667`

Обычный `exec_command` и `apply_patch` внутри sandbox сначала падали с `helper_unknown_error: setup refresh had errors`. Read/write операции были выполнены через PowerShell с явной эскалацией. Это tooling issue, не изменение проекта.

## Прочитанный scope

По запросу M23 была ограничена область чтения:

- `AGENTS.md`
- `CODEX_CONTEXT.md` отсутствовал
- `docs/architecture/decisions/ADR-019-random-effects-reml-wald-quantitative-synthesis.md`
- M22 quantitative implementation:
  - `QuantitativeSynthesisContracts.cs`
  - `FixedEffectQuantitativeStatisticalSynthesizer.cs`
  - `RandomEffectsQuantitativeStatisticalSynthesizer.cs`
  - `StandardNormalQuantile.cs`
- SynthesisContext/prompt contracts:
  - `SynthesisContextContracts.cs`
  - `SynthesisContextBuilder.cs`
  - `ResearchSynthesisPrompt.cs`
- corresponding tests:
  - `QuantitativeStatisticalSynthesizerTests.cs`
  - `ResearchSynthesisTests.cs`
  - `FullFakePipelineTests.cs`

## Методологическая проверка

Проверен официальный reference `metafor rma.uni`: `test="knha"` и `test="hksj"` описаны как Knapp-Hartung/Hartung-Knapp-Sidik-Jonkman inference с adjustment к standard errors и t/F distributions. Для моделей используется `k - p` degrees of freedom. В MedResearch M23 нет модераторов и модель intercept-only, поэтому `p = 1`, а `df = k - 1`.

M23 реализует canonical HKSJ без ad-hoc ограничения. Поэтому adjustment меньше 1 не зажимается до Wald variance, и HKSJ CI может быть уже Wald CI.

## Реализованная формула

M23 использует уже рассчитанные M22 random-effects данные:

```text
w_i_RE = 1 / (v_i + tau²)
theta_RE = sum(w_i_RE * theta_i) / sum(w_i_RE)
Var_Wald(theta_RE) = 1 / sum(w_i_RE)
df = k - 1
q_HKSJ = sum(w_i_RE * (theta_i - theta_RE)^2) / df
Var_HKSJ(theta_RE) = q_HKSJ * Var_Wald(theta_RE)
SE_HKSJ = sqrt(Var_HKSJ)
CI_HKSJ = theta_RE +/- t_(1-alpha/2, df) * SE_HKSJ
```

Для OR/RR/HR все вычисления выполняются на log-scale. Reported-scale point estimate и HKSJ CI endpoints получаются через `exp(...)` после analysis-scale расчета.

## Изменения в коде

- Добавлен `StudentTQuantile` с inverse CDF через regularized incomplete beta и bisection.
- Добавлен `HksjSummaryEffectInferenceCalculator`.
- Добавлен `QuantitativeConfidenceIntervalMethod.HartungKnappSidikJonkman`.
- Добавлен `QuantitativeHksjInferenceResult`.
- Добавлены explicit failure reasons для HKSJ.
- `RandomEffectsQuantitativeStatisticalSynthesizer` сначала строит прежний Wald result, затем считает HKSJ поверх него. Wald поля M22 не изменяются.
- `SynthesisContext` получил `SynthesisHksjInferenceContext` внутри `SynthesisRandomEffectsResultContext`.
- `ResearchSynthesisPrompt` теперь включает `HksjInference` рядом с Wald random-effects output.
- System prompt прямо запрещает LLM рассчитывать или изменять HKSJ.
- `AGENTS.md` получил короткий invariant: HKSJ является deterministic Application code, не LLM calculation.

## Проверенные случаи

Application tests покрывают:

- `k = 1`: HKSJ unavailable, `df = 0`.
- `k = 2`: `df = 1`, Student-t critical value около `12.7062047364321`.
- `k >= 3`: BCG reference dataset.
- `tau² = 0`: HKSJ использует canonical variance adjustment и может быть уже Wald без ad-hoc clamp.
- positive `tau²`: BCG reference dataset.
- order independence.
- non-finite contribution inputs.
- Wald result remains unchanged.
- HKSJ point estimate equals M22 random-effects point estimate.
- SynthesisContext propagation.
- prompt includes deterministic HKSJ and still forbids LLM calculation.
- full fake pipeline prompt receives HKSJ.

## Reference values

BCG fixture remains the existing metafor reference fixture from M19-M22. M23 expected values are based on `metafor` `rma(..., method="REML", test="knha")` semantics:

- `theta_RE ~= -0.714528384090743`
- `df = 12`
- `t_0.975,12 ~= 2.17881282966342`
- `q_HKSJ ~= 1.01137`
- `SE_HKSJ ~= 0.18079`
- analysis-scale HKSJ CI approximately `-1.10844` to `-0.32062`
- reported-scale HKSJ CI approximately `0.33007` to `0.72570`

The test tolerance accounts for MedResearch using the full internal REML tau² estimate while common printed metafor examples round tau² to four decimals.

## Что намеренно не сделано

- Нет modified/ad-hoc HKSJ.
- Нет prediction intervals.
- Нет tau² CI.
- Нет p-values.
- Нет automatic inference/model selection.
- Нет schema/persistence changes.
- Нет изменений M17 fixed-effect, M18 heterogeneity, M19 tau² или M22 Wald semantics.

## Замеченный дефект и исправление

Severity: medium.

Problem: в первой версии HKSJ calculator для `k=1` вычислялся `df = 0`, но unavailable result возвращал `DegreesOfFreedom = null`.

Fix: unavailable HKSJ result теперь сохраняет `df = k - 1`, то есть `0` для single-study boundary.

Test: `Hksj_ReturnsUnavailableForSingleStudyBecauseDegreesOfFreedomAreZero`.

## Verification log

Промежуточно:

- `dotnet build MedResearch.slnx --no-restore`: сначала падал только из-за нового тестового использования `Assert.NotNull` как возвращающего helper; исправлено под текущий xUnit style.
- `dotnet test tests\MedResearch.Application.Tests\MedResearch.Application.Tests.csproj`: сначала выявил округление BCG constants и `df=null` boundary; оба исправлены.
- После исправлений Application tests: `145 passed, 0 failed, 0 skipped`.

Финальная полная проверка записывается отдельно в итоговом ответе после полного regression и CI.
