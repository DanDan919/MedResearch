# Milestone 19 — REML tau-squared foundation

## 1. Зачем этот слой

После M17 и M18 у MedResearch уже был узкий deterministic quantitative path: совместимые OR/RR/HR findings превращались в log-scale contributions, затем считался fixed-effect inverse-variance pooled result, а поверх того же набора contributions считались Cochran's Q, df и I².

M19 добавляет следующий строительный блок: оценку between-study variance, то есть `tau²`. Это не random-effects meta-analysis целиком. Это только фундаментальное численное значение, которое в будущем может понадобиться для random-effects weights, prediction intervals или других методов.

## 2. Что именно считается

`RestrictedMaximumLikelihoodTauSquaredEstimator` решает REML score equation на analysis scale.

Для текущих OR/RR/HR групп analysis scale — это log-ratio scale. Поэтому `tau²` хранится как variance на squared log scale, а не как raw odds ratio/risk ratio/hazard ratio.

## 3. Почему REML

Актуальный Cochrane Handbook описывает `tau²` как between-study variance и указывает, что REML доступен в RevMan с 2024 года и является default estimator для between-study variance. Документация `metafor` также использует `method="REML"` как стандартный путь для random-effects model fitting и публикует BCG reference example.

M19 берет только estimator foundation, не всю random-effects интерпретацию.

## 4. Численная стратегия

Я выбрал bounded bisection, а не Newton/Fisher scoring.

Причина простая: для этого milestone важнее deterministic recovery behavior, чем скорость. Bisection требует bracket, зато после bracket она монотонно сужает интервал и не делает больших неожиданных шагов.

Алгоритм:

1. Проверить, что есть минимум две независимые Study contributions.
2. Проверить finite effects и finite positive variances.
3. Посчитать REML score в `tau² = 0`.
4. Если score не positive, вернуть валидное boundary estimate `tau² = 0`.
5. Иначе расширять finite upper bound, пока score не станет неположительным.
6. Решить root bounded bisection с max iteration и tolerance.
7. Если bracket или convergence не получились, вернуть `NotEstimated`, не подменяя результат нулем.

## 5. Trust boundary

LLM не считает `tau²`. LLM получает уже рассчитанное значение в `SynthesisContext` и prompt explicitly запрещает модели считать или заменять:

- pooled effect;
- heterogeneity statistic;
- between-study variance / tau-squared;
- random-effects weights;
- random-effects pooled estimate;
- p-value;
- confidence interval.

## 6. Что не изменилось

M19 не меняет:

- fixed-effect pooled log effect;
- fixed-effect variance;
- fixed-effect standard error;
- fixed-effect confidence interval;
- fixed-effect contribution weights;
- Cochran's Q;
- df;
- Q-derived I².

Именно поэтому M19 можно рассматривать как additive foundation layer.

## 7. Что не реализовано

Намеренно не реализовано:

- random-effects weights;
- random-effects pooled estimate;
- random-effects confidence interval;
- HKSJ;
- prediction interval;
- Q-profile tau² interval;
- tau-based replacement for I²;
- automatic model selection;
- forest/funnel plots.

## 8. Reference test

Основной numerical reference test использует BCG dataset из документации `metafor`: `escalc(measure="RR")`, затем `rma(yi, vi, method="REML")`, ожидаемый `tau² ≈ 0.3132`.

Этот expected value не генерируется production code MedResearch.

## 9. Почему нет миграции

M17/M18 quantitative outputs уже были transient read models. M19 следует тому же правилу. Persisted Evidence, SourceMaterial, Study lineage и algorithm versions достаточны для воспроизводимости на этом этапе.

Persisted quantitative result snapshot можно добавить позже, когда появится стабильный report/API contract для machine-readable quantitative synthesis.
