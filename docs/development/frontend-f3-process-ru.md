# Frontend F3: observability исполнения ResearchRun

## Старт

Фактическое начало работы:

- ветка: `main`
- HEAD: `825e8a30c539a92c2cfb89f6d4c3d8f8ed23185c`
- upstream: `origin/main`
- remote: `https://github.com/DanDan919/MedResearch.git`
- рабочее дерево перед изменениями: чистое

Перед изменениями были прочитаны frontend-документы, F2 process log, `frontend/AGENTS.md`, backend lifecycle, worker/lease code, EF persistence, synthesis/extraction/evaluation stores и текущий frontend detail view.

## Аудит backend state

Существующее состояние уже хранит:

- `ResearchRun.Status`, `CreatedAt`, `StartedAt`, `CompletedAt`, `FailureReason`;
- lease timestamps/version: `ProcessingLeaseExpiresAt`, `LastHeartbeatAt`, `ProcessingLeaseVersion`;
- `ResearchPlan.SearchQueries`;
- `LiteratureSearch` per source/query;
- `ResearchStudyDiscovery` per search/study path;
- global `Study`;
- global `SourceMaterial` per Study/version;
- run-scoped `EvidenceExtraction`, `Evidence`, `EvidenceEvaluation`;
- run-scoped `ResearchReport` and `ResearchReportClaim`.

Не хранится:

- процент выполнения;
- ETA;
- live provider activity;
- точная failed stage после перехода `ResearchRun.Status = Failed`;
- отдельная per-run source acquisition ledger.

Поэтому F3 не стал добавлять fake progress. Endpoint показывает только persisted факты и честно оставляет неизвестное неизвестным.

## Backend design

Добавлен endpoint:

```text
GET /api/research/{researchRunId}/progress
```

Application:

- `GetResearchProgressUseCase`;
- `IResearchProgressStore`;
- `ResearchRunProgress` read model.

Infrastructure:

- `EfResearchProgressStore`.

Счетчики считаются по `researchRunId`, кроме `SourceMaterial`: он глобальный для `Study`, поэтому считается через Studies, discovered текущим run. Это значит “материал доступен для найденных в run исследований”, а не “материал был создан только этим run”.

Failure stage не выдумывается. Для `Failed`/`Cancelled` stages показывают completed только там, где есть persisted output; сама ошибка отображается отдельно как terminal state + safe failure reason.

Schema migration не потребовалась.

## Frontend design

`/research/[id]` теперь использует progress endpoint через:

- OpenAPI snapshot;
- generated TypeScript;
- Zod schema;
- `MedResearchApiClient.getResearchProgress`;
- `useResearchProgress`;
- polling только пока статус не terminal.

UI показывает:

- status pill;
- backend-provided pipeline stage list;
- persisted counters for Search/Evidence/Synthesis;
- run metadata;
- processing lease state;
- safe failure panel;
- report link only for `Completed`.

Frontend не считает:

- scientific/statistical values;
- HKSJ/REML/prediction intervals;
- evidence quality;
- percentages;
- ETA;
- failed stage.

## Tests

Добавлены/обновлены:

- Application tests for active lease, expired lease, and failed-run no-fake-stage behavior.
- API integration tests for `/progress` 200/404 through `WebApplicationFactory`.
- PostgreSQL/Testcontainers tests for persisted progress counters and run-scoped evidence isolation.
- API package client test for progress request path.
- Web component tests for progress observatory, completed report navigation, and failure panel.

Локально PostgreSQL tests skip из-за недоступного Docker Desktop engine. CI должен выполнить их реально, как и предыдущие milestones.

## Ограничения

- Нет WebSocket/SSE; используется polling.
- Нет cancel/retry buttons.
- Нет study explorer.
- Нет live provider status.
- Нет stage duration model кроме общих timestamps.
- Failed stage не persisted, поэтому не показывается как факт.
- SourceMaterial shared across runs by design.

## Проверка на момент локальной работы

Уже прошло:

- `dotnet build MedResearch.slnx --no-restore`
- `dotnet test MedResearch.slnx --no-build` with local Docker-backed skips
- `pnpm typecheck`
- `pnpm lint`
- `pnpm test`
- `pnpm build`
- `pnpm --filter @medresearch/desktop vite:build`

Оставшиеся перед завершением стандартные проверки:

- restore;
- EF pending model;
- docker compose config;
- git diff check;
- CI after push.
