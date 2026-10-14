# QUICK-Q4.1: идемпотентное создание

Дата: 14.10.2026. Исходный фактический HEAD `3b27a3b4baedbd3a232d802196d74a64eeb2fe59`, main. Реализация проверена отдельно в `E:\MedResearch-Q41-verify`; параллельные Deep/M24/M25/M26/UI изменения сохраняются и не входят в Q4.1.

## Исправленный риск

Q4 блокировал double-click только в одной форме. Потеря HTTP-ответа после commit, повтор с другого клиента или одновременные POST могли создать несколько QuickResearchRuns. Блокировка retry в UI не давала гарантии серверной идемпотентности.

## Контракт

`POST /api/quick-research` требует non-empty D-format UUID `Idempotency-Key` и `{ "question": "..." }`. Владелец берётся только из существующего authenticated immutable subject, не из тела/headers. BFF передаёт проверенный ключ и только server-side bearer; CSRF, bounded input, private/no-store и safe Location сохранены.

- Owner + key + тот же accepted trimmed question: 202, исходный Run ID и **текущее** состояние, включая Completed/Failed. На Queued ничего не сбрасывается.
- Owner + key + другой question: безопасный 409, `admission-idempotency-conflict`, без текста вопроса/fingerprint в ошибке.
- Different owner + тот же key: отдельный Run. Different key + тот же question: намеренная новая заявка, а не дедупликация по тексту.
- Missing/invalid/empty key: 400 до persistence. В OpenAPI header required, UUID; SDK regenerated.

Переиспользованы существующий key parser и безопасное отображение rejection, **не** Deep admission transaction, quota policy или scientific pipeline.

## PostgreSQL

Forward migration: `20261014140047_AddQuickCreationIdempotency`. В существующей `quick_research_runs` добавлены nullable `creation_key` UUID и `creation_fingerprint` varchar(64). EF shadow properties оставляют operational persistence metadata вне Domain и public result DTO.

Unique filtered index `ux_quick_owner_creation_key(owner_subject_id, creation_key) WHERE creation_key IS NOT NULL`; CHECK требует связную пару, non-empty key и 64 hex characters. Nullable нужен только для сохранения historical rows: старые ключи не выдумываются/backfill не делается. Старые migrations не менялись. В snapshot изменён только Quick entity; устранён посторонний Windows escaped-newline churn generated metadata.

Короткая READ COMMITTED transaction: `INSERT … ON CONFLICT DO NOTHING` → чтение owner/key и fingerprint → validated current-run projection → commit. PostgreSQL unique index сериализует конкурирующие inserts между API replicas; следующий statement видит committed winner. Нет process-local locks/advisory lock framework/новой таблицы/очереди/внешнего I/O внутри transaction. Conflict/error/cancellation rollback не оставляет orphan row или consumed key.

Fingerprint: SHA-256 UTF-8 от `quick-create-v1\n` + accepted question, uppercase hex. Только outer trim; case, internal whitespace и Unicode не переписываются. Дополнительной копии sensitive question text нет. Hash не является шифрованием против guessing. Worker stages/lease SQL не меняют creation metadata.

## Browser replay

Форма сохраняет один key для одной canonical заявки. Immediate in-flight guard стоит до async hashing. После unknown response доступен ручной Retry, автоматических повторов нет. После успешного ответа key очищается; новая форма/другая question получает новый key.

Session-scoped sessionStorage содержит только key + hash, не question или bearer. Это позволяет после reload повторно ввести тот же вопрос и вернуть исходный Run. При недоступном storage работает mounted-form fallback; durability через reload в таком браузере не обещается. Нет cross-device автопоиска ключа, текстового matching или глобального patient history в браузере. Account/session namespaces изолированы; tab storage заканчивается с жизнью tab. Локальный hash также не скрывает известный вопрос от dictionary guessing.

## Фактические проверки

- Native PostgreSQL 18.3 + настоящий production Next/BFF/Kestrel/JWT/EF/worker: **13 passed / 0 failed / 0 skipped**, 14 unique runs. Источники и LLM строго fake.
- Browser fault injection делает настоящий upstream POST, получает committed 202 и обрывает доставку ответа, **не** подменяет API JSON. Reload/re-entry посылает исходный key и получает исходный ID; intentional next submission получает другой key/ID.
- 12 параллельных actual BFF POST дают один Run; конфликт 409 не увеличивает историю; другая JWT-сессия с тем же key получает отдельный Run и reciprocal foreign 404.
- Fresh DbContext/audit проверяет два fake searches и не более одного generation на каждый unique run, без redispatch после replays и без terminal lease.
- Дополнительные PostgreSQL/Testcontainers tests: 12 конкурентных requests на двух независимых API hosts; конфликт/owner isolation/new key; Completed/Failed replay; response lost + API restart; реальный PostgreSQL 22012 перед commit, rollback и повтор; прямое нарушение unique index (23505). Не InMemory/SQLite.
- Локальный backend: Domain62/Application437/Infrastructure234/Integration68 passed, 0 failed; 189 Docker-required tests честно skipped из-за недоступного Docker. Это **не** результат выполнения этих tests; их authoritative gate — CI.
- SDK77/Web209 unit passed, Chromium27 passed, frontend lint/typecheck/production build passed. EF pending-model, Compose config и OpenAPI regeneration comparison проходят. CI также сохраняет existing Deep full-stack и OIDC/BFF regressions.

CI запускается на committed Q4.1 с `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true`; required skip означает failure. Финальные run ID, counters, push и HEAD приводятся после фактического наблюдения результата в итоговом отчёте, не выводятся из старого green Q4.

## Границы

Все API replicas должны быть обновлены после migrations; старые binaries/manual SQL могут создавать historical-style rows без ключа и не входят в гарантию. Manual delete/update, потеря БД/backup restore или потеря client key не создают magical exactly-once guarantee. Автоматического purge или reset ключей при terminal status нет.

Q4.1 не ограничивает число разных заявок, расходы или clinical correctness. RU query/relevance и abstract-only/entailment ограничения Q1/Q2 сохраняются. Live/paid scientific requests: **0**; T01–T08 не выполнялись. Deep algorithms, источники, stages, billing и general quota framework не менялись.

Следующий приоритет — фактическая научная продуктовая приёмка T01–T08 с отдельным явным разрешением и ограничениями live-запуска. Q4.1 заканчивается здесь; автоматического расширения архитектуры нет.
