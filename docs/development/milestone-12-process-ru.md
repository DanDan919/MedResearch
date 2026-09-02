# Milestone 12 Process Log: Europe PMC Multi-Source Retrieval

Date: 2026-09-02

## Старт

Работа началась с фактического HEAD `b0e5e7c` на ветке `main`. Это отличается от старого ориентира `f1da607`, потому что после него уже был завершен PubMed hardening. Историю не переписывал и откаты не делал.

## Архитектурная проверка

Сначала была проверена текущая модель: `Study` хранит глобальную идентичность публикации, а `LiteratureSearch` и `ResearchStudyDiscovery` отвечают за provenance конкретного поиска. Это оставалось главным инвариантом при добавлении второго источника.

Официальная документация Europe PMC подтвердила, что для Articles REST API нужен production base `https://www.ebi.ac.uk/europepmc/webservices/rest/`, endpoint `/search`, параметр `query`, `format=json`, `resultType=core`, `pageSize` и cursor pagination через `cursorMark` / `nextCursorMark`.

## Решения

- Добавлен `EuropePmcScientificLiteratureSource`, но PubMed не переписан заново.
- Добавлен `ScientificLiteratureSearchCoordinator`, чтобы цикл по источникам был в одном Application-boundary, а не размазан по pipeline.
- Один query против PubMed и Europe PMC создает две записи `LiteratureSearch`.
- PMCID добавлен в `Study` как nullable stable identifier с filtered unique index.
- Identity resolution использует только PMID, PMCID и DOI.
- Если stable identifiers указывают на разные Studies, кандидат пропускается как hard conflict.
- Provider record identity сохраняется как provenance, но не используется для fuzzy merge.

## Проверка

Добавлены deterministic fake HTTP тесты Europe PMC для request parameters, JSON `core` mapping, cursor pagination, retry, cancellation, malformed JSON, zero-result behavior and deduplication. Добавлены PostgreSQL integration tests для PMCID identity, multi-source discovery paths, hard identifier conflict и downstream one-study work item.

Локально Docker недоступен, поэтому PostgreSQL tests честно skipped. CI должен быть авторитетной проверкой Testcontainers, как в milestone 10/11.

## Осторожности

Live Europe PMC smoke test добавлен отдельно от `MedResearch.slnx` и запускается только при `MEDRESEARCH_RUN_LIVE_EUROPEPMC_TESTS=true`. Нормальный CI не зависит от Europe PMC availability и не ходит в интернет.
