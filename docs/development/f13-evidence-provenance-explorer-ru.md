# F13: Evidence & Provenance Explorer

## Цель

F13 добавляет только read-only представление уже сохранённых фактов. Новый экран не выполняет научные вычисления, не вызывает PubMed/Europe PMC/OpenAI и не пытается восстановить отсутствующие цитаты по заголовку или похожести текста.

Маршруты:

- `GET /api/research/{researchRunId}/provenance`
- `/research/{researchRunId}/evidence`

Доступ к API проходит через текущую аутентификацию. Если запуск принадлежит другому субъекту, read model не возвращается.

## Как читать граф

```text
ResearchRun
  -> LiteratureSearch
      -> ResearchStudyDiscovery
          -> Study
              -> SourceMaterial metadata
              -> EvidenceExtraction
                  -> Evidence
              -> EvidenceEvaluation
  -> ResearchReportClaim -> Evidence IDs
  -> quantitative contribution -> Evidence/Study/extraction/source IDs
```

`Study` является глобальной идентичностью публикации. `LiteratureSearch` описывает один конкретный запуск поиска у одного источника. `ResearchStudyDiscovery` описывает один путь от поиска к Study. Поэтому одна публикация, найденная двумя запросами или двумя источниками, должна быть показана один раз с двумя discovery paths.

Extraction, Evidence, Evaluation, report claims и quantitative contributions относятся к выбранному `ResearchRun`. EF projection фильтрует их по этому run. Для claim-to-Evidence связей есть дополнительная проверка `Evidence.ResearchRunId`, чтобы случайная повреждённая межзапусковая ссылка не стала видимой пользователю.

## Что показывает экран

1. Summary counters: поиски, discovery paths, distinct studies, source material snapshots, extractions, Evidence, evaluations и claims.
2. Search executions: источник, запрос, число результатов, число сохранённых study paths, дубликаты и статус успешного результата.
3. Studies: PMID/PMCID/DOI только если они реально сохранены, журнал, год, авторы и источник.
4. Discovery paths: отдельные источник, provider identifier и запрос для каждого search execution.
5. SourceMaterial metadata: тип, provider, retrieval method, hash, версия, current flag, access status, character count, truncation и секции.
6. Extraction, Evidence и Evaluation отдельными блоками. Если extraction не дал валидного Evidence, экран говорит `No validated Evidence is available`.
7. Report claims со ссылками на реальные Evidence IDs и quantitative contribution lineage.

`SourceMaterial.Content` намеренно не входит в DTO и не выбирается EF query. Это снижает риск случайного раскрытия больших текстов и сохраняет границу: экран показывает provenance, а не ещё один просмотрщик исходной статьи.

## Честные ограничения

Текущая модель `LiteratureSearch` не хранит отдельную failed-attempt запись, если provider request упал до создания успешного search record. Поэтому API возвращает `hasPersistedProviderFailureProvenance=false`. Нулевой результат поиска и provider failure не объявляются одним и тем же: нулевой результат сохраняется как успешный search с `ResultCount=0`; failure history пока просто не существует в persistence.

`SourceMaterial` глобален для Study. Поэтому экран может показать несколько сохранённых версий материала, но не утверждает, что каждая версия была загружена именно выбранным запуском. Точная версия, использованная extraction, видна через `EvidenceExtraction.SourceMaterialId`.

## Проверки

- Application/API: owner authorization и 404 для чужого run.
- PostgreSQL: глобальный Study, несколько discovery paths, run-scoped Evidence и report claim graph из нового DbContext.
- Frontend API: Zod contract и run-scoped URL/query key.
- Playwright: multi-source discovery, zero-evidence state, отсутствие raw source body, claim links и quantitative contribution links.

Обычные тесты не вызывают внешние научные сервисы. PostgreSQL/Testcontainers остаётся обязательным в CI и может быть пропущен локально только при недоступном Docker daemon.
