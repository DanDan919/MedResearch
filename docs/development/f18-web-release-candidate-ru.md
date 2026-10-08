# F18: Web Release Candidate и проверка границ развёртывания

Дата: 2026-10-08. Статус checkpoint: реализация и локальная регрессия завершены;
новый full-stack runtime и итоговый CI ещё НЕ подтверждены. Этот документ не
объявляет release/deployment готовым до получения соответствующих результатов.

## 1. Исходное состояние

Самостоятельно проверены HEAD `39a9bd9963ddc41a53c3d1700a91d798ca1e20ab`,
ветка `main`, upstream `origin/main`, точный remote
`https://github.com/DanDan919/MedResearch.git`, чистое рабочее дерево.
Успешный baseline CI: `37790433685`. История не переписывалась.
F18 не изменяет M17-M24, F15.1/F15.2/F16 научные алгоритмы/валидаторы,
схему БД, существующие миграции или правила source grounding.

## 2. Что независимая проверка опровергла

1. Production OIDC tests F17 использовали синтетический HTTP API, не реальный
   ASP.NET/PostgreSQL. Нельзя считать их доказательством full-stack ownership.
2. Старый browser harness обходил проверку своего сертификата. Это не
   доказательство доверенного HTTPS. F18 добавляет отдельный no-bypass runner.
3. Проверка генерации TS из curated snapshot не ловила drift реального backend.
4. Mobile shell реально расширял документ до 575px при viewport 320/375/390.
   Это воспроизведено и в development, и в patched production сборке ДО CSS fix.
5. Два Zod поля допускали null, невозможный в текущем backend DTO. Моки ошибочно
   подтверждали более широкий контракт. Null negative controls теперь явные.
6. Запуск audit сам по себе не исправлял production HIGH. Потребовались реальные
   опубликованные patch versions и повторная регрессия, не исключение advisory.

## 3. Dependencies: полный audit без фильтрации

До: production 2 HIGH / 4 MODERATE / 1 LOW / 0 CRITICAL = 7.
Полный audit: 3 HIGH / 4 MODERATE / 1 LOW / 0 CRITICAL = 8.
После: production все severity 0. Полный audit: 1 HIGH dev-only, остальные 0.

| Advisory | Severity | Пакет/диапазон | Исправление / reachability |
| --- | --- | --- | --- |
| GHSA-cjq9-62q9-8jv4 | HIGH | Next >=16.0.0 <16.3.8 | 16.3.8; Image SSRF требует remotePatterns, здесь отсутствует, но patch установлен |
| GHSA-3w37-wq28-93x7 | MODERATE | Next >=16.3.0 <16.3.8 | 16.3.8; use cache/DraftMode не используется |
| GHSA-4jqv-mc3x-m676 | MODERATE | Next >=16.0.0 <16.3.8 | 16.3.8; Pages SSG/ISR отсутствует |
| GHSA-39w2-rjm5-chcv | LOW | Next >=16.0.0 <16.3.8 | 16.3.8; dev MCP origin boundary |
| GHSA-f87g-xv8r-7p7x | MODERATE | Next >=16.0.0 <16.3.8 | 16.3.8; metadata-image webpack path здесь не используется |
| GHSA-mcj8-r9mp-w47p | MODERATE | Next >=16.0.0 <16.3.8 | 16.3.8; root catch-all SSG/ISR отсутствует |
| GHSA-68fv-2mgg-jv7q | HIGH | source-map-js >=1.0.0 <1.2.2 | 1.2.2; malicious indexed source map DoS |
| GHSA-vfj7-8cjw-p6xm | HIGH | braces <=3.0.3 | Published patch отсутствует; только lint dev dependency |

Полные точные диапазоны, описание и dependency paths сохраняются без
сокращений в audit JSON (`dependency-audits` CI artifact). Таблица выше
выделяет применимую установленную версию, а не заменяет advisory database.
Next путь: web -> next. source-map-js: web -> next -> postcss, desktop ->
@vitejs/plugin-react -> vite -> postcss; CSS tooling closure также обновлена.
braces: @next/eslint-plugin-next -> fast-glob -> micromatch -> braces и
eslint-config-next -> @next/eslint-plugin-next -> fast-glob -> micromatch -> braces.

Проверены first-party advisory/registry и опубликованные patches:
[Next advisory index](https://github.com/vercel/next.js/security/advisories),
[source-map-js advisory](https://github.com/advisories/GHSA-68fv-2mgg-jv7q),
[source-map-js 1.2.2 release](https://github.com/7rulnik/source-map-js/releases/tag/v1.2.2),
[braces advisory](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm),
[maintainer discussion](https://github.com/micromatch/braces/issues/70).

Исправлены только Next 16.3.6 -> 16.3.8 и source-map-js 1.2.1 -> 1.2.2
с необходимыми platform closures. Auth libraries не заменены; peers не
форсировались. Blind lockfile regeneration первоначально обновил unrelated
latest-зависимости; это исправлено structured baseline merge, frozen install
подтверждён. Нет overrides/ignore advisories/удаления test coverage.

`pnpm security:audit` сохраняет полный и production JSON, блокирует production
HIGH/CRITICAL, malformed/unavailable audit fail-closed. `security:test`: 5
negative/positive policy cases. Dev-only HIGH не скрыт и не объявлен исправленным.
В traced standalone output каталог braces отсутствует. Публичного runtime
входа для lint glob нет; patterns контролирует репозиторий. Mitigation: isolated
CI, не принимать пользовательские lint patterns, сохранить advisory и ожидать
настоящий upstream patch. Fork/repository код всё равно недоверенный.

## 4. Production trust topology и тестовая граница

Browser -> HTTPS proxy :3441 -> Next standalone :3440 -> реальный ASP.NET
Production JWT API :3442 -> свежий PostgreSQL 17 Testcontainers.
Synthetic HTTPS OIDC issuer/JWKS :3443; все listeners loopback.
Временная CA доверяется Chromium NSS, Node extra CA и .NET SSL certificate file.
CA и serverAuth/SAN leaf раздельные; OpenSSL проверяет цепочку, localhost и IP.
Изолированный XDG_DATA_HOME использует текущий Chromium NSS путь. Проверена
[официальная Chromium документация](https://chromium.googlesource.com/chromium/src/+/main/docs/linux/cert_management.md).
`ignoreHTTPSErrors:false`; нет NODE_TLS_REJECT_UNAUTHORIZED=0 или certificate
callback, возвращающего true. Старый F17 bypass suite остаётся отдельным,
не используется как доказательство trusted HTTPS F18.

OIDC: code + S256 PKCE + state/nonce, confidential client_secret_post, RS256,
отдельный audience JWT. Cookie __Host-, Secure, HttpOnly, SameSite=Lax;
expiry ограничивается JWT и локальной политикой. Refresh/global logout/
revocation не добавлялись. Logout/switch очищают клиентский scientific cache.
API самостоятельно проверяет issuer/audience/signature/lifetime, owner из sub.
Небезопасный Authority URL в Production отклоняется без печати URL/credentials.

17 новых full-stack cases: trusted HTTPS и persisted reads/restart; User A/B
history/detail/progress/report/quantitative/provenance isolation; POST queued
ResearchRun; CSRF/redirect/path restrictions; шесть bad-login вариантов;
session expiry; issuer outage/corrupt cookie/forged bearer; API/PG outages;
пять populated viewport cases. Live endpoint controls существуют ТОЛЬКО в
изолированном fixture, не в production API. Worker в browser API выключен.

Seed использует существующий fake structured LLM и fake scientific literature/
full-text adapters через настоящие use cases/EF stores. Реальный Completed run
имеет report, grounded evidence, evaluations, quantitative snapshot и source
lineage. Отдельные 97 Studies/discoveries расширяют read-model до 100; три
исходных Study имеют по два discovery path. Эти 97 строк НЕ создают evidence
или новые научные claims и НЕ изображают полный 100-study synthesis.
Никаких live OpenAI/PubMed/Europe PMC/платных calls. Browser guard допускает
только loopback. API restart должен вернуть идентичные report/artifact snapshots.

Database outage: live 200, ready 503, scientific read не 200. API outage:
BFF 503 и видимый error, не empty scientific history. Cleanup ограничен
собственными processes/container/temp path. Local Docker failure не заменяется
SQLite/InMemory и не превращается в skip этого runner.

## 5. Backend-origin OpenAPI

Actual Development WebApplicationFactory -> /openapi/v1.json -> canonical JSON
-> openapi-typescript -> client TS -> отдельные Zod runtime refinements.
Сравнение исключает только runtime servers и сортирует object keys; array
order, paths, enums, security, required и nullable не игнорируются.
Явный regeneration flag отсутствует в CI. Четыре mutation tests меняют
path/schema/security/nullability и обнаруживаются. Actual-document test и
mutation tests: 5 passed. Девять frontend alignment controls проверяют
wire/Zod assignability, required/null, Bearer metadata и unknown status.
Production OpenAPI endpoint не включён.

ASP.NET schema transformer исправляет nullable/numeric schema metadata
согласно реальной JSON сериализации и добавляет фактическую authorization
metadata. Это не меняет scientific DTO payload. Citation ResultSummary и
StudySource обязательные строки; отсутствующие bibliographic identifiers,
journal, date и authors не выдумываются. Generated TS вручную не редактировался.

## 6. Responsive / accessibility

Причина 575px: implicit CSS grid min-content tracks для navigation/sidebar.
Исправление: явные minmax(0,1fr) tracks, min-w-0 и overflow-wrap:anywhere для
длинных UUID/hash/title; компактный API status сохраняет sr-only label.
Нет global overflow-x:hidden, clipping научных данных или уменьшения ширины
измерения. Existing tables/forest plot сохраняют собственные scroll containers.
ErrorPanel имеет role=alert. Навигация на mobile горизонтально прокручивается.

Матрица 320/375/390/768/1280 на dashboard/new/history/detail/report/evidence/
quantitative/settings/login = 45 проверок в каждой оболочке. Финальный
full-stack matrix дополнительно использует persisted report/artifact/100Study
provenance, длинные значения, filter и проверяет один provenance request,
а не N+1. Screenshot и scrollWidth/clientWidth JSON сохраняются в CI.
Нет reliance на networkidle для polling страницы: visible state + fonts.ready.
Hydration/pageerror regression сохранена. Это не полный WCAG certification.

## 7. Packaging / operator handoff

Next output standalone с monorepo tracing root, минимальный server.js и static
assets; Docker Node24/pnpm11.19, non-root runtime, runtime env secrets.
API Dockerfile прежний. CI собирает оба images; это не image vulnerability scan.
.dockerignore исключает env/cache/node_modules/test outputs из build context.
`docs/frontend/deployment.md` описывает private listeners, exact Host proxy,
runtime JWT/OIDC/session конфигурацию, отдельное применение migrations,
ready/live, fail-closed auth, shutdown/diagnostics и safe secret injection.
Development Compose не выдаётся за production deployment template.

## 8. Локальная проверка

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Domain | 45 | 0 | 0 |
| Application | 289 | 0 | 0 |
| Infrastructure | 110 | 0 | 0 |
| Integration | 47 | 0 | 103 |
| Frontend API | 40 | 0 | 0 |
| Web | 99 | 0 | 0 |
| Security policy | 5 | 0 | 0 |
| Scientific Chromium | 15 | 0 | 0 |
| Production OIDC Chromium | 17 | 0 | 0 |

Backend 491 passed, 103 local Docker skips. Restore/build: success, 0 warnings/
errors; EF pending-model: нет изменений; Compose config и diff check проходят.
Docker info: Linux engine pipe недоступен. Новый full-stack runner локально
явно FAILED на Docker availability, не заявлен как passed. Его Release build
успешен. Frontend lint/typecheck/build/desktop Vite успешны. UI/desktop не имеют
отдельных unit test cases; отсутствие cases не выдаётся за passed coverage.
Native Tauri НЕ проверен. Screenshot production shell 320px просмотрен.

## 9. CI и Git: результат дополняется после запуска

Workflow `.github/workflows/ci.yml`: Ubuntu, .NET10, Node24/pnpm11.19.
Три jobs: existing strict build-test, frontend и изолированный web-release.
Required PG suite ожидается 103 выполненных cases, 0 required skips. Backend
total ожидается 594; эти числа пока ожидание, не утверждение результата CI.
Full-stack 17 cases/trusted CA, image builds и diagnostics artifact обязательны.
Первый commit `20354cd1be259951748b5722ceb98be413c75ffd`, CI `37807605255`:
frontend/build-test success, 594 backend passed, 0 failed/skipped. Новый
web-release runner FAILED до браузера: неверный content root WebApplicationFactory
в console entrypoint (без normal test manifest). Явный src/MedResearch.Api test
root исправляет этот harness дефект; итоговый runtime CI остаётся pending.
Второй run `37808486190` на `596f22aa1688c3d170ef2b6b4e7635813052557e`
дошёл до Planning, где неизменённый validator отверг несовпадающий seed question.
Seed/fake planner используют общий constant; validator не ослаблен. Остальные
jobs второго run success. Это ошибки новой fixture подготовки, не основание
изменять научные инварианты или объявлять full-stack passed.
Третий run `37809117190` достиг API/browser, но 17 cases отклонили тестовый
сертификат (ERR_CERT_INVALID). CA/server leaf были одним certificate. Цепочка
заменена на отдельную CA/serverAuth/SAN leaf; NSS путь актуализирован без
TLS bypass. До повторного runtime CI это исправление не объявляется verified.

## 10. Оставшиеся ограничения / следующий milestone

External real IdP, real deployed HTTPS и native Tauri: NOT VERIFIED. Нет
OpenAI/live scientific workflow, не доказано качество реальной выдачи провайдеров.
Неполная metadata/source acquisition provenance не исправлена этой задачей.
Stateless cookie revocation, token/secret rotation, script CSP/XSS, quotas,
image digest pinning/scanning требуют отдельной работы. Dev braces advisory
сохраняется с описанной reachability/mitigation, не suppression.

Рекомендуется ровно один следующий milestone: узкая проверка реального
HTTPS deployment с operator-provided IdP/configuration, двумя пользователями
и приватным API/PostgreSQL. Не запускается автоматически.
