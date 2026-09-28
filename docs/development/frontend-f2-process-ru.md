# Frontend F2: история исследовательских запусков

## Цель

F2 добавляет реальную историю `ResearchRun` вместо честного placeholder-состояния на `/research`.

Ключевой принцип сохранен: frontend отображает состояние backend и не вычисляет научные результаты локально.

## Что изменено

- Добавлен backend endpoint `GET /api/research`.
- Endpoint поддерживает `page`, `pageSize` и optional `status`.
- Добавлен application use case `ListResearchRunsUseCase`.
- EF-хранилище проецирует только summary-поля и не загружает тяжелые графы Evidence/Report/Study.
- Добавлен индекс `ix_research_runs_created_at_id` для общего newest-first списка.
- OpenAPI snapshot обновлен, TypeScript-клиент перегенерирован.
- `/research` теперь показывает backend-backed список запусков, фильтр по статусу и пагинацию.
- После создания нового исследования frontend invalidates history query.

## Контракт API

`GET /api/research` возвращает:

- `items`
- `page`
- `pageSize`
- `totalCount`
- `totalPages`

Каждый item содержит:

- `researchRunId`
- `researchQuestionId`
- `question`
- `status`
- `createdAt`
- `startedAt`
- `completedAt`
- `failureReason`

Невалидные `page`, `pageSize` и `status` должны возвращать `400`.

## Границы ответственности

Frontend не добавляет:

- локальную научную логику;
- локальную реконструкцию pipeline;
- локальные расчеты Evidence/Evaluation/Synthesis/Quantitative;
- прямые вызовы OpenAI, PubMed, Europe PMC или full-text источников.

## Найденная проблема после перезапуска

После восстановления сессии `pnpm typecheck` падал, потому что новые элементы API package были добавлены в файлы `status.ts` и `types.ts`, но не экспортированы из `frontend/packages/api/src/index.ts`.

Исправление:

- экспортированы `researchRunStatusPresentation`;
- экспортирован `hasActiveResearchRuns`;
- экспортированы `ResearchRunListFilters`, `ResearchRunListResponse`, `ResearchRunSummaryResponse`.

## Тесты

Добавлены проверки:

- API client формирует `GET /api/research?page=...&pageSize=...&status=...`.
- API package публикует стабильные query keys и helper активных статусов.
- Web UI показывает empty state.
- Web UI показывает список запусков и ссылку на detail page.
- Web UI применяет status filter через URL.
- Web UI показывает ошибку backend.
- Playwright smoke проверяет `/research` с mock backend response.

Backend-тесты покрывают:

- defaults/validation/status parsing use case;
- PostgreSQL pagination newest-first;
- deterministic tie-breaker по id;
- status filter;
- API 200/400 behavior.

## Риски и ограничения

- Study browser API по-прежнему отсутствует.
- Report list API по-прежнему отсутствует.
- Frontend OpenAPI snapshot пока обновляется вручную и затем генерирует TypeScript-контракт.
- Local Docker может быть недоступен; PostgreSQL/Testcontainers остаются авторитетно проверяемыми в CI.
