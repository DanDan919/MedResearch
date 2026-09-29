# F4: рабочее пространство научного отчёта

## Цель

F4 не добавляет новую научную обработку. Цель milestone — сделать существующий экран отчёта полезным для проверки трассируемости: пользователь должен видеть, какое утверждение было сохранено, какие Evidence его поддерживают и к какой публикации относится каждое Evidence.

## Что проверено в backend

Источник истины начинается с `GET /api/research/{researchRunId}/report`. Endpoint сначала проверяет существование ResearchRun. Поэтому неизвестный run возвращает `404`, а известный run без готового отчёта возвращает `409`. Это разные состояния: второе означает «результат ещё не готов», а не ошибку научного процесса.

Сохранённый граф имеет следующую форму:

```text
ResearchReport
  -> ResearchReportClaim
      -> ResearchReportClaimEvidence
          -> Evidence
              -> EvidenceExtraction -> SourceMaterial (metadata only)
              -> Study
```

`EfResearchSynthesisStore` собирает этот граф фиксированным числом запросов. При чтении citation projection дополнительно проверяются `ResearchRunId` Evidence и EvidenceExtraction, соответствие Study и SourceMaterial, а содержимое SourceMaterial не выбирается. Это защищает read-model от случайной cross-run ссылки даже при некорректной edge-записи.

Приложение уже проверяет, что модель synthesis ссылается только на Evidence текущего SynthesisContext и текущего ResearchRun. API не принимает PMID, DOI или StudyId от модели как authoritative citation data.

## Что добавлено в API read-model

Citation теперь возвращает только сохраненные поля:

- Study: title, journal, publication date parts, authors, publication types, PMID, PMCID, DOI и source;
- Evidence: outcome, result summary, supporting text, direction, source scope, grounding flag и доступные структурированные поля;
- SourceMaterial lineage: type, provider, retrieval method, version, retrieved timestamp, access status, truncation flag и section names.

Содержимое SourceMaterial, raw XML/JSON, lease metadata и внутренние persistence-поля в report endpoint не раскрываются. Схема OpenAPI и сгенерированный TypeScript-контракт обновлены вместе с Zod runtime validation.

## Frontend workspace

Экран `/research/{id}/report` теперь состоит из:

1. Заголовка отчёта с вопросом, статусом и временем генерации.
2. Блока persisted coverage: найденные, извлечённые и оценённые Studies, включённые findings и источники поиска.
3. Narrative-секций отчёта: summary, evidence summary, conflict summary, limitations и conclusion.
4. Упорядоченного списка claims.
5. Native `<details>` для каждого citation. В раскрытом состоянии видны Study metadata, identifiers, extracted result/supporting text, доступные Evidence fields и SourceMaterial lineage.

PMID, PMCID и DOI становятся внешними ссылками только при наличии значения в API. Для отсутствующих полей показывается `Not available`, а отсутствующие идентификаторы не заменяются placeholder-ссылками. Браузер не вычисляет доверительные интервалы, confidence score, pooled effects или какие-либо новые научные показатели.

Кнопка печати вызывает обычный print dialog. Навигация и print control скрываются в print media, а раскрытые evidence-блоки сохраняются в печатаемом представлении.

## Тестовая матрица

- Backend integration: API возвращает расширенную citation projection; endpoint различает `404` и `409`.
- PostgreSQL report-store: authoritative metadata восстанавливается свежим DbContext; намеренная cross-run claim/evidence edge не попадает в report projection.
- Frontend unit: completed report, expandable evidence, external PMID link, 409 not-ready, 404 unknown run и missing identifiers.
- Playwright: direct deep-link/refresh report route, happy path, not-ready path и отсутствие ссылок для missing identifiers.

Все frontend tests используют mock fetch. Они не вызывают OpenAI, PubMed, Europe PMC, full-text endpoints или произвольную сеть.

## Границы и оставшиеся gaps

F4 не добавляет report list endpoint, отдельный provenance explorer, raw SourceMaterial browsing или автоматическую связь citation с EvidenceEvaluation. Evaluation можно показать в будущем только через явный backend read-model contract, а не через догадки frontend. Полнотекстовые материалы также намеренно не выводятся пользователю на этом экране.
