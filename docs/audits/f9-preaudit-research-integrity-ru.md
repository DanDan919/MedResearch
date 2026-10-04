# F9: предварительный аудит research-фич и проверка утверждений

Дата аудита: 2026-10-04
Репозиторий: `E:\\MedResearch`
Remote: `https://github.com/DanDan919/MedResearch.git`
Ветка: `main`
Проверенный на момент аудита HEAD: `7d8e74f` (`docs: record F8 verification results`)

## Зачем нужен этот документ

Это adversarial-аудит, а не подтверждение маркетингового состояния проекта. Для каждого важного утверждения я отделял:

- **подтверждено** — найдено в коде и/или воспроизведено тестом;
- **частично подтверждено** — есть реализация, но отсутствует важная проверка или есть ограничение;
- **не подтверждено** — этого нельзя утверждать по текущему локальному запуску;
- **дефект** — наблюдаемое поведение противоречит заявленной retry/traceability-гарантии.

## Проверенное состояние

Рабочее дерево на checkpoint было чистым, `main` совпадал с `origin/main`. HEAD отличается от более ранних F7/F8 baseline, поэтому старые CI-цифры не считаются автоматически результатом текущего коммита.

Локально подтверждено:

- Domain: 26 passed, 0 failed, 0 skipped;
- Application: 163 passed, 0 failed, 0 skipped;
- Infrastructure: 65 passed, 0 failed, 0 skipped;
- Integration: 17 passed, 0 failed, 75 skipped из-за недоступного Docker Desktop Linux engine;
- всего backend: 271 passed, 0 failed, 75 skipped;
- frontend API: 11 passed;
- frontend web: 22 passed;
- frontend lint и typecheck: passed;
- Playwright: 7 passed локально.

Локальные PostgreSQL/Testcontainers-тесты не были выполнены: они пропущены честно, потому что Docker daemon недоступен. Поэтому этот аудит **не заявляет новую независимую PostgreSQL runtime confidence**. Исторические CI-результаты из документации проекта остаются историческими и требуют повторной проверки после F9.

## Что действительно подтверждено кодом

### Слоистость

Зависимости соответствуют заявленной направленности: Domain не знает EF Core/ASP.NET/HTTP-провайдеров; Application использует provider-neutral контракты; Infrastructure содержит EF Core, PostgreSQL, PubMed и Europe PMC адаптеры; API выступает composition root и HTTP-слоем.

### Контур научной трассируемости

Подтверждены следующие границы:

- `Study` является глобальной публикационной сущностью;
- `Evidence`, evaluation и report привязаны к `ResearchRun`;
- `ResearchStudyDiscovery` сохраняет путь конкретного `LiteratureSearch` к `Study`;
- LLM не является источником доверенных PMID/DOI;
- quantitative-расчеты выполняются детерминированным кодом и передаются в synthesis context;
- frontend quantitative workspace читает сохраненные артефакты и не считает статистику в браузере;
- Playwright и API-клиент используют run-scoped запросы.

### Runtime execution

Atomic claim, lease owner/version, heartbeat, `FOR UPDATE SKIP LOCKED` и write fencing присутствуют. Production stores открывают короткую транзакцию перед проверкой fencing token; транзакция не удерживается вокруг внешних вызовов. Это подтверждает наличие механизма, но не доказывает, что все crash windows и повторные stage executions безопасны.

## Найденные дефекты и пробелы

### P1: повторная Planning после crash не идемпотентна

`src/MedResearch.Infrastructure/Planning/Persistence/EfResearchPlanStore.cs` в `SaveResearchPlanAsync` безусловно добавляет новый `ResearchPlan`. При этом `ResearchPlanConfiguration` задает unique index по `ResearchRunId`.

Сценарий:

1. worker сохраняет план;
2. процесс падает до перехода `Planning -> Searching`;
3. lease истекает, другой worker возобновляет текущую стадию;
4. planner генерирует или загружает план заново;
5. второй insert получает unique violation, и run может стать `Failed`.

Это противоречит заявленной recovery-модели “resume current stage”. Нужна идемпотентная запись с проверкой run, prompt version и входного snapshot. Конфликтующие данные должны отклоняться, а не молча заменять существующий план.

### P1/P2: повторная Searching создает новые внешние executions

`ScientificResearchStageExecutor` запускает coordinator заново при каждом входе в `Searching`. `ScientificLiteratureSearchCoordinator` создает новый `searchExecutionId` и вызывает source adapter снова.

Сценарий после crash между успешной записью поиска и переходом стадии:

- повторный PubMed/Europe PMC request;
- новый `LiteratureSearch`;
- повторные discovery rows или лишняя provenance history;
- дополнительная нагрузка на внешнего провайдера.

Глобальная Study deduplication смягчает риск неправильной публикационной идентичности, но не делает search stage retry-idempotent. Нужен детерминированный execution key, например `(ResearchRunId, ResearchPlanId, query, source)`, и reuse уже успешно сохраненного execution.

### P2: partial provider failure теряет provenance результата ошибки

Coordinator логирует исключение одного source и продолжает, если другой source успешно отработал. В БД сохраняются успешные `LiteratureSearch`, но отдельный failed attempt/status не сохраняется. Если все source упали, stage завершается исключением.

Такое поведение допустимо только если оно явно принято архитектурой. Сейчас модель `LiteratureSearch` и search contracts в основном описывают успешные результаты, поэтому нельзя надежно ответить, какой source был attempted, failed, timed out или вернул zero results. F9 должен выбрать явную модель статуса и покрыть ее тестами, не смешивая zero results с operational failure.

### P2: пустой evidence corpus помечается как abstract-only

В `src/MedResearch.Application/Research/Synthesis/SynthesisContextBuilder.cs` условие вычисляет `UsesAbstractLevelEvidenceOnly = true`, когда `selectedEvidence.Length == 0`.

Отсутствие evidence не означает наличие abstract-level evidence. Это создает ложный сигнал для report/UI и должно быть исправлено на `selectedEvidence.Length > 0 && ...`. Нужен отрицательный тест для пустого корпуса и проверка insufficient-evidence report.

### P2: Playwright не является CI-проверкой

`.github/workflows/ci.yml` запускает frontend install, API generation diff, lint, typecheck, unit tests и builds, но не запускает `pnpm test:e2e` и не устанавливает Playwright browser dependencies.

Локальные 7 Playwright тестов прошли, однако это не равно CI-гарантии. F9 должен добавить отдельный deterministic browser step. Тесты используют mocked API и не требуют OpenAI, PubMed или PostgreSQL.

## Самокритика и проверка на галлюцинации

Я не считаю следующие утверждения доказанными этим аудитом:

- что текущий HEAD уже прошел PostgreSQL/Testcontainers suite в CI после F8;
- что все 7 Playwright тестов уже запускаются в GitHub Actions;
- что recovery после crash безопасен на всех стадиях;
- что partial provider failures полностью traceable;
- что production system имеет authentication/authorization;
- что live OpenAI/PubMed/Europe PMC runtime проверен.

Старые документы F8 содержат сильные формулировки и CI identifiers, но они не заменяют повторное выполнение на текущем commit. Локальный Docker недоступен, поэтому skipped integration tests нельзя интерпретировать как passed.

Также важно: найденные дефекты не доказывают, что каждый crash обязательно приведет к повреждению научных данных. Они доказывают более узкое и достаточное для F9 утверждение: заявленная idempotent recovery guarantee не следует из текущего кода.

## Приоритет F9

1. Сделать Planning persistence idempotent и fenced.
2. Сделать Searching execution idempotent по run/plan/query/source и явно решить partial-failure provenance.
3. Исправить пустой-evidence semantic flag.
4. Включить Playwright в CI.
5. Добавить negative/concurrency tests и повторно подтвердить PostgreSQL suite в CI.

Реализацию этих исправлений намеренно не выполнял в рамках текущего запроса. Англоязычный prompt для отдельного F9 находится в `docs/development/f9-research-integrity-recovery-hardening-prompt-en.md`.
