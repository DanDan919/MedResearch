# QUICK-Q3: простой интерфейс сохранённых ответов

## Область и исходное состояние

Исходный HEAD: `d3805bb7d8ff0ca999fb933acb719c0eabac38ac`, ветка `main`,
remote `https://github.com/DanDan919/MedResearch.git`.
Q0/Q1/Q2 проверены по коду, а не только по предыдущему отчёту.
Основное дерево содержит параллельные Deep/M24/UI/audit изменения.
Разработка и регрессия Q3 выполнены в отдельном checkout от указанного HEAD;
перенос и staging ограничены явным списком Q3-файлов. Чужие изменения не
сбрасывались, не прятались в stash и не включались в Q3.

## Страницы и путь данных

`/quick-research` показывает owner-scoped историю с пагинацией.
`/quick-research/[id]` показывает оригинальный вопрос, короткий ответ,
findings, сохранённые противоречия, ограничения, uncertainty и источники.
В существующую навигацию добавлен Quick Research; маршруты Deep не изменены.
Основной ответ находится перед подробностями. Пустой блок противоречий не
показывается. Нет создания, повторного запуска или Quick-to-Deep conversion.

Путь чтения:

```text
Next page -> QuickResultWorkspace / QuickHistory
  -> useQuickResearch / useQuickHistory (React Query)
  -> MedResearchApiClient
  -> /api/backend/api/quick-research[/{id}] (существующий BFF)
  -> owner-scoped ASP.NET API -> сохранённый Q0/Q1/Q2 результат
```

SDK добавляет только `getQuickResearch` и `listQuickResearch`.
Generated OpenAPI types не редактировались; backend API, алгоритмы,
провайдеры, воркеры и схема PostgreSQL не изменялись. Миграций Q3 нет.
Новая Zod-проекция в `packages/api/src/quick.ts` совместима с generated
контрактами и проверяет ограниченную форму, статус, run lineage и ссылки
citations на выбранные источники. Это транспортная защита, не повторная
медицинская валидация backend и не доказательство entailment.

## Состояния без выдуманных результатов

| Сохранённое состояние | Отображение |
| --- | --- |
| Queued | Три ожидающие фазы, без процентов и ETA |
| Processing, retrieval ещё не final | Finding studies активна |
| Processing, final retrieval | Finding studies закончена; Comparing findings и Preparing answer активны вместе |
| Completed + Succeeded | Сохранённый ответ и его citations |
| Completed + InsufficientEvidence | Явная недостаточность, без invented findings |
| Completed без answer | No answer available; Q0 synthetic result не выдаётся за медицинский ответ |
| Failed | Безопасное сообщение, уже выбранные источники сохранены в UI |
| Partial retrieval | Предупреждение о неполном поисковом покрытии |
| ZeroResults | Отсутствие результатов ограниченного поиска, не утверждение об отсутствии исследований |
| NeedsClarification | Вопрос нельзя искать без угадывания смысла |
| HTTP 404 | Один ответ для чужого и неизвестного run |
| HTTP/contract failure | Санитизированная ошибка чтения и GET refresh |

Q2 не хранит отдельные реальные стадии сравнения и оформления: это один
вызов генерации. Поэтому UI не заявляет, что сравнение уже завершено, пока
готовится ответ. Эти три подписи являются простым представлением известного
состояния, а не новыми научными или worker-стадиями.
Polling: detail 5 секунд, history 10 секунд только при активных запусках.
Терминальные результаты не опрашиваются периодически; refresh не выполняет
retrieval или LLM. Нет автоматического retry запроса к провайдерам.

## Источники и цитаты

Кнопка `[n]` у finding/contradiction открывает диалог именно соответствующего
источника и сохранённой supporting excerpt. Кнопка заголовка в списке
источников показывает все уникальные сохранённые выдержки этого источника.
Диалог поддерживает клавиатуру, Escape, focus trap и возврат фокуса к кнопке.
Показываются только доступные journal, publication date/year, authors и IDs.
Год без полной даты остаётся годом; отсутствующие дата, авторы и DOI не
синтезируются. Abstract не превращается в supporting excerpt при отсутствии
citations. Conflicting metadata отмечается отдельным предупреждением.

Внешний URL не конструируется frontend: проверяется уже переданный API URL.
Допускаются только HTTPS, идентификатор-согласованные пути
`pubmed.ncbi.nlm.nih.gov/{PMID}/`, `europepmc.org/articles/{PMCID}` и
`doi.org/{DOI}`. Credentials, иной host/port, query/hash и invalid encoding
отклоняются. Нет URL -> нет активной ссылки. Используются
`noopener noreferrer` и `referrerPolicy=no-referrer`.
Ссылка на источник доказывает lineage, но не медицинскую корректность вывода.

## Безопасность и границы

- Quick BFF allowlist разрешает только GET list/detail; POST/PATCH/DELETE,
  чужие query-параметры, повторные параметры и неподдерживаемые подмаршруты
  отклоняются до API. Same-origin/Fetch Metadata проверяются и для Quick GET.
- Bearer остаётся в приватной серверной сессии. Browser bearer, owner headers
  и cookies не пересылаются upstream как identity. Owner фильтрует backend.
- Ответы BFF private/no-store; SDK fetch no-store и same-origin credentials.
- Query keys включают opaque sessionId и runId/page. Нет placeholder старого
  ответа при смене run/account. Result remount закрывает старую excerpt.
- Existing auth/logout/broadcast очистка продолжает применяться; Quick login
  return path внесён в существующий безопасный allowlist.
- Model/source строки отображаются как React text, без HTML injection.
- Технические детали закрыты по умолчанию; raw failureCategory/safeError,
  prompt, connection strings и provider transcripts не показываются.
- UI read-only; никаких новых live-provider/paid-LLM вызовов или настроек.

## Детерминированная проверка

Тестовые публикации имеют явную synthetic маркировку и находятся только в
test fixtures. Runtime приложения их не импортирует. OIDC/HTTP fixtures
работают на loopback и не обращаются к OpenAI, PubMed или Europe PMC.

Локальная проверка на изолированном Q3 checkout:

| Проверка | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| API SDK Vitest | 74 | 0 | 0 |
| Web Vitest (включая BFF) | 192 | 0 | 0 |
| Chromium development | 27 | 0 | 0 |
| Chromium production HTTPS/OIDC | 19 | 0 | 0 |
| Security policy | 5 | 0 | 0 |
| Offline deployment preflight | 40 | 0 | 0 |
| .NET Domain | 62 | 0 | 0 |
| .NET Application | 437 | 0 | 0 |
| .NET Infrastructure | 234 | 0 | 0 |
| .NET Integration | 67 | 0 | 182 |

Q3 добавил 29 SDK cases, 35 web/BFF cases, 7 development browser cases и
2 production browser cases. Проверены completed/insufficient/no-answer,
Queued/Processing/Failed, partial/zero/clarification, citation excerpts,
отсутствующая metadata, HTML text, unsafe URLs, чужой run, приватные JWT
сессии User A/B, account switch и очистка второго таба. Desktop 1440x900 и
mobile 390x844 проверены screenshot и overflow assertions; длинный русский
вопрос и неразрывный заголовок не создают горизонтальный page overflow.
Старые Deep unit/browser suites проходили вместе с новыми тестами.

`pnpm lint`, `pnpm typecheck`, `pnpm build`, `pnpm api:generate`/generated
drift check, `dotnet restore`, `dotnet build` (0 warnings/errors), EF
pending-model check, Compose config и `git diff --check` прошли.
`docker info` локально не прошёл: отсутствует pipe Docker Desktop Linux
engine. 182 PostgreSQL skips не названы runtime verification.

Первый Chromium прогон обнаружил отсутствие возврата фокуса после Escape:
исправлено сохранением конкретной trigger-кнопки и `onCloseAutoFocus`.
Негативный browser test также уточнён, чтобы не путать сообщение ошибки со
служебным Next route announcer. Повторный полный прогон зелёный.

## CI

Используется существующий `.github/workflows/ci.yml`, без новых workflows:
Frontend, Build and test, Actual API PostgreSQL HTTPS web RC.
Linux/.NET 10/Node 24; `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true` должен
обеспечить реальные PostgreSQL tests без required skips.
Фактически проверенный Q3 run:
[38262188656](https://github.com/DanDan919/MedResearch/actions/runs/38262188656),
commit `fb0137befba2ea1f9aeb24ff2c1f9204b7c0969e`, итог `success`.
Все три jobs завершились `success`. TRX annotations:

| Suite в CI | Executed | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: | ---: |
| Domain | 62 | 62 | 0 | 0 |
| Application | 437 | 437 | 0 | 0 |
| Infrastructure | 234 | 234 | 0 | 0 |
| Integration | 249 | 249 | 0 | 0 |

Итого backend: 982 passed, 0 failed, 0 skipped. В отличие от локального
прогона, все 182 Docker-required PostgreSQL cases реально выполнены.
Docker info, EF pending-model verification и Compose validation в CI прошли;
full-stack job проверил actual API, freshly migrated PostgreSQL, production
Next/HTTPS и контейнерные сборки. Frontend job проверил deterministic Quick
и Deep suites, Chromium и production OIDC/BFF, без live medical providers.
CI workflow и PostgreSQL tests не ослаблялись ради зелёного статуса.

После проверки добавлена только эта запись доказательств отдельным docs
коммитом Q3. Его CI проверяется отдельно перед финальным отчётом.

## Локальный просмотр

Для просмотра поднят изолированный development UI:
`http://127.0.0.1:3000/quick-research` (HTTP 200). Локальный API на 5252
запущен с отключёнными Deep/Quick workers, retrieval/answer и startup
миграциями, поэтому просмотр не инициирует live или paid provider calls.
Однако `GET /health/ready` локально вернул `503 Unhealthy`: текущая локальная
PostgreSQL конфигурация API не прошла connectivity check. Это не объявлено
успешным локальным DB runtime verification; изменение пароля/данных или
ремонт локальной БД не выполнялся. Пока readiness не восстановлена, UI
показывает честную ошибку чтения вместо synthetic results. PostgreSQL
проверка Q3 подтверждена CI, не этим локальным просмотром.

## Ограничения и следующий шаг

Q3 не делает answer медицински валидированным, не расширяет поиск и не
включает live providers. Backend Q2 ограничения, abstract-only scope,
bounded retrieval и запрет model-authored numeric prose остаются.
Язык общего web shell остаётся английским; вопрос/сохранённый ответ могут
быть русскими. Формальная user research/clinical safety оценка не выполнена.
Результаты появляются только для ранее созданных Quick runs текущего owner;
создание через UI намеренно вне Q3. Schema изменений нет.

Единственная рекомендуемая следующая задача: Q4 — детерминированная
adversarial проверка безопасности и понятности Quick ответов до разрешения
live execution. Q4 здесь не реализуется.
