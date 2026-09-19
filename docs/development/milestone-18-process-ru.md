# Milestone 18 — Heterogeneity Diagnostics

## 1. Зачем M18 появился после M17

M17 научил MedResearch аккуратно объединять совместимые ratio effects через fixed-effect inverse-variance модель. Но pooled effect сам по себе не говорит, насколько отдельные Study estimates разбросаны вокруг этого pooled estimate. M18 добавляет диагностический слой: Cochran's Q, df и I².

## 2. Что такое statistical heterogeneity

Statistical heterogeneity — это наблюдаемый разброс effect estimates относительно статистической модели. Она не объясняет причину различий. Это не clinical heterogeneity, не methodological heterogeneity и не оценка качества исследований.

## 3. Cochran's Q

Реализованная формула:

`Q = sum(w_i * (theta_i - theta_pooled)^2)`

где `theta_i` — analysis-scale effect конкретного Study contribution, `theta_pooled` — M17 pooled analysis-scale effect, а `w_i` — тот же inverse-variance weight, который использовался для pooling.

## 4. Почему используются те же weights

Q должен описывать разброс в рамках той же модели, которая дала pooled result. Поэтому M18 не добавляет quality weights, source weights или random-effects weights. Используются ровно M17 weights.

## 5. Degrees of freedom

`df = k - 1`, где `k` — число независимых Study contributions в successful M17 synthesis result. Так как M17 требует минимум две независимые Study, `df >= 1`.

## 6. I²

Внутреннее представление — proportion `0..1`.

Если `Q <= df` или `Q == 0`, I² возвращается как `0`.

Иначе:

`I² = (Q - df) / Q`

## 7. Пример Q < df

Если наблюдаемый weighted dispersion не превышает degrees of freedom, формула могла бы дать отрицательное значение. Такое значение не имеет полезного смысла для I², поэтому оно bounded at zero.

## 8. Пример Q = 0

Если все analysis-scale effects одинаковы, Q равен нулю. M18 обрабатывает это явно и возвращает I² = 0 без деления на ноль, NaN или Infinity.

## 9. Почему I² не quality score

I² не говорит, что часть исследований плохая или ложная. Это не reliability score и не probability того, что pooled result неверен.

## 10. Почему I² = 0 не доказывает отсутствие heterogeneity

I² = 0 означает, что наблюдаемый statistic после zero bound не показывает excess dispersion в этой выборке и модели. Особенно при малом числе Studies это не доказывает, что истинные эффекты идентичны.

## 11. Почему M18 не делает random effects

Random effects требуют отдельных решений: tau² estimator, random-effects weights, prediction interval, model-selection policy. M18 намеренно оставляет это будущему milestone.

## 12. Provenance

Diagnostics считаются только по exact contributions успешного M17 result. Один Study не может попасть дважды, а multiple discovery paths/provider provenance не увеличивают `k`.

## 13. LLM boundary

LLM получает уже рассчитанные deterministic values в prompt context. Prompt запрещает модели считать собственный meta-analysis, Q, I², confidence interval или заменять supplied values.

## 14. Tests

Добавлены reference tests для Q > df, Q <= df, Q = 0, k = 2, log-scale calculation, order independence, non-finite inputs и non-positive weights. Fake vertical E2E теперь использует три compatible OR studies и проверяет Q/df/I² по independent formula.

## 15. Ограничения

M18 не добавляет tau², Q p-value, random-effects pooled result, prediction interval, forest plot, funnel plot, publication-bias tests или persistence table для diagnostics.

## 16. Следующий milestone

Следующий логичный milestone: Random-effects foundation decision and tau-squared estimator audit. Его не нужно смешивать с M18.
