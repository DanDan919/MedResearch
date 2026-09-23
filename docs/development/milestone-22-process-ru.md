# Milestone 22 process log: REML random-effects Wald synthesis

## Старт восстановления

Работа началась с dirty tree на `main` при HEAD `2c98d6935872e6d541160bb242bf1c39603e079d`. Незавершенная M22-работа уже содержала изменения в quantitative contracts, fixed-effect synthesizer, synthesis context contracts и новый файл `RandomEffectsQuantitativeStatisticalSynthesizer.cs`. Эти изменения были сохранены и не откатывались.

## Что было незавершено

Уже существовал черновой random-effects synthesizer с формулой `1 / (vi + tau²)` и подключение к `FixedEffectQuantitativeStatisticalSynthesizer`, но integration была неполной: `SynthesisRandomEffectsResultContext` отсутствовал, context builder не маппил random-effects result, prompt не показывал M22 значения, тесты и документация не покрывали новую ветку.

Dirty-tree baseline:

- `dotnet restore`: passed.
- `dotnet build --no-restore`: failed with missing `SynthesisRandomEffectsResultContext`.
- `dotnet test --no-build`: old binaries passed, therefore only diagnostic baseline, not proof of current source correctness.

## Методология

Проверены Cochrane Handbook Chapter 10, RevMan statistical settings and `metafor::rma.uni`. M22 реализует только REML random-effects inverse-variance estimate with standard-normal Wald confidence interval. HKSJ, prediction intervals and tau² confidence intervals remain deferred.

## Реализация

M22 reuses the validated M17 contribution population and M19 REML tau-squared estimate. It adds a separate nested random-effects result to the existing common/fixed-effect synthesis result. No database persistence or migration was added.

## Тестирование

Added deterministic tests for:

- positive tau² BCG/metafor reference dataset;
- `tau² = 0` collapse to fixed/common effect;
- tau² NotEstimated propagation;
- invalid tau² and non-finite input rejection;
- duplicate Study contribution rejection;
- normalized random-effects weights;
- order independence;
- reduction of relative weight spread when tau² is positive;
- SynthesisContext and prompt propagation;
- fake vertical pipeline prompt/context visibility.

## Ограничения

M22 is not a recommendation engine and does not choose between common/fixed-effect and random-effects views. It does not implement HKSJ, prediction intervals, tau² confidence intervals, subgroup analysis, meta-regression, publication bias methods, forest plots, or persisted quantitative result artifacts.
