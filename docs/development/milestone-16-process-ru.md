# Milestone 16 — Live Scientific E2E Validation & Hardening

## 1. Цель

Проверить готовность MedResearch к реальной scientific E2E validation без включения live вызовов в обычный CI. Этап не добавляет новый научный источник и не меняет scientific claims.

## 2. Safety baseline

Стартовая точка: ветка main, HEAD e673f2a0ef1f32965d5a44452305cc4ce322d18b, рабочее дерево clean. До изменений restore/build/test/EF pending model/compose config прошли локально; Docker daemon локально недоступен, поэтому PostgreSQL/Testcontainers tests skipped как и раньше.

## 3. Live E2E boundary

Добавлен отдельный проект `tests/MedResearch.LiveE2EValidationTests`, который не входит в `MedResearch.slnx`. Он skipped по умолчанию и требует `MEDRESEARCH_RUN_LIVE_E2E=true`, OpenAI model/API key, PubMed contact email и явное подтверждение `MEDRESEARCH_LIVE_E2E_DATABASE_ACK=isolated`.

## 4. Production path

Harness не вызывает adapters напрямую как замену E2E. Он поднимает API через `WebApplicationFactory<Program>`, использует production DI и hosted worker, вызывает `POST /api/research`, ждёт terminal ResearchRun state и читает `GET /api/research/{id}/report`.

## 5. Bounded validation

Для live run снижены runtime bounds: `ResearchPlanning:MaxSearchQueries=2`, provider result caps 5, source acquisition/extraction/evaluation caps 5, synthesis cap 5 studies / 20 findings / 8 claims. Это проверка архитектуры на реальных данных, а не exhaustive literature review.

## 6. Planning hardening

Раньше planner validator имел только hard-coded максимум 5 queries. Добавлен `ResearchPlanning:MaxSearchQueries` с default 5, а prompt schema, prompt text и validator теперь используют configured bound. Это предотвращает нежелательный live fan-out без доверия к модели.

## 7. Secrets

Live harness не хранит и не печатает OpenAI key. Ключ и модель приходят только из environment/configuration. `.env` не добавляется и не требуется.

## 8. Normal CI

Normal solution tests и GitHub Actions остаются deterministic и не делают live OpenAI/PubMed/Europe PMC/full-text requests. Live E2E запускается отдельной командой только вручную.

## 9. Ограничение

Live E2E не был выполнен в этой среде, потому что нет configured OpenAI key и локальный Docker/PostgreSQL runtime недоступен. Harness подготовлен для запуска на явно изолированной PostgreSQL базе.
