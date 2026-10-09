# F19: реальный HTTPS/OIDC deployment - подготовка и граница доказательств

Дата: 2026-10-09. Gate A: implementation/локальная проверка выполняются;
финальный deterministic CI пока pending. Gate B: NOT RUN.
Этот checkpoint не объявляется real deployment verified.

## Baseline и сохранность

Фактический HEAD: `1cebd49cfb8c07d7b20665f35ce8344b3e4c91cc`, `main`,
upstream `origin/main`, remote `https://github.com/DanDan919/MedResearch.git`.
Status short пустой; log25 и diff check проверены. Baseline CI `37930193705`
был success с 599 backend passed/0 failed/0 skipped. Эти числа исторические,
не результаты нового F19 CI. История и чужие файлы не переписывались.

Прочитаны AGENTS, architecture/current-state, ADR-025/032, F17/F18 reports,
authentication/deployment guides, actual Next config/proxy/session/OIDC/BFF,
ASP.NET authentication/minimal endpoints/options, Docker/Compose и CI.

## Наличие реального окружения

Проверена только presence конфигурации, без вывода значений/секретов. В процессе
отсутствуют WEB_AUTH_ORIGIN, internal API URL, OIDC issuer/client/secret/audience/
scope, session secret, API authority/audience и DB connection. Нет root .env/
.env.production или web .env.local/.env.production/.env.production.local.
Это отсутствие обнаруженной конфигурации в workspace, НЕ утверждение, что
оператор не имеет внешнего сервера/аккаунта где-либо ещё.

Actual hostname, real issuer/client registration, два разрешённых аккаунта,
staging server/access и точное разрешение внешних действий не предоставлены.
Provider не выбран: операторские варианты отсутствуют. Не заявляется, что
Keycloak/Entra/Auth0 defaults подходят к существующему generic профилю.
Нет cloud/DNS/IdP/user/deployment/configuration changes, внешних probes/login,
remote POST, миграций или key rotation. Browser Gate B не запускался.

## Actual topology и requirements

Browser -> реальный HTTPS proxy -> private Next standalone/BFF -> private
Production ASP.NET JWT API -> private PostgreSQL. External OIDC остаётся внешним,
не встроенным issuer. Лишь web origin публичный; proxy сохраняет exact Host,
очищает forwarded headers и не кеширует private data. Next строит callback из
configured webOrigin, не forwarded host. API не получает authoritative owner
headers из браузера и независимо проверяет JWT issuer/aud/signature/lifetime.

Generic профиль: code/S256 PKCE/state/nonce, client_secret_post, RS256 ID token,
отдельный RS256/PS256/ES256 access JWT. API aud отличается от client ID, access aud
не включает client ID; access/ID sub совпадают. Один issuer, стабильный bounded
sub, два различных реальных пользователя. Pairwise metadata не доказывает
subject alignment. Opaque/basic-only/incompatible audience не обходятся SDK.

Exact issuer включая slash должен совпадать в Next/API. Существующие limits:
access JWT <=3000 chars, cookie <=4KB, timeout 1..30sec/default10, session
60..3600sec/default3600 и не позже exp. BFF clock skew 0, API framework 5min.
Нельзя применять ID token как bearer. Нет refresh/offline_access/global revoke/
graceful multi-key cookie rotation. Замена cookie secret инвалидирует сессии.

## Gate A implementation

Добавлен `frontend/scripts/deployment-preflight.mjs` и 40 deterministic tests.
Production auth/научный код не менялись. Через Node24 native type stripping
переиспользуются actual `readAuthConfig` и bounded-body reader; новый validator
web config и новая dependency не создаются. Дополнительные checks относятся
к cross-process issuer/audience, Production modes, DB config presence, worker
explicit false и TLS bypass. Connection-string parser/DB permissions этим не
верифицируются. Secret length не доказывает entropy.

`pnpm deployment:preflight` по умолчанию offline. Actual workspace: exit1,
passed=false, network=NOT RUN; семь missing/unsafe checks. Это ожидаемый блокер
deployment preflight, а не доказательство production defect. Positive synthetic
control проходит. Вывод только names/booleans/status/categories, без config values.

Opt-in `--network-approved` плюс каждый exact `--allow-origin=` допускают только
anonymous GET discovery/JWKS/web-session/private API live/ready/401. Без разрешённых
origins HTTP calls=0. Redirects не следуются; body1MB/auth deadline; metadata не
может запросить unapproved JWKS origin. Нет credentials/cookies/POST/token exchange.
HTTP errors/TLS/body/JSON errors не раскрывают remote bodies/URLs/secrets.
Real login/owner isolation/rollover остаются NOT RUN даже при probe success.

Metadata exact issuer/HTTPS/code/client_secret_post/RS256/subject/S256 проверяются
консервативно. Missing S256 advertisement требует ручного доказательства, а не
объявления провайдера несовместимым. Public JWKS shape не доказывает реальную
JWT signature, API audience или rotation. Node trust не сертифицирует browser TLS.

Официальные документы:
[OIDC Discovery](https://openid.net/specs/openid-connect-discovery-1_0.html),
[Node native TypeScript](https://nodejs.org/api/typescript.html).
Node24.19.0 фактически импортировал существующий config без transpiler.
CLI требует `--conditions=react-server` только для server-only imports в tool;
browser/production boundary не ослаблен.

## Проверка себя / найденная documentation discrepancy

В authentication guide утверждалось, что старый F17 browser harness не отключает
TLS. Actual playwright.auth.config.ts имеет ignoreHTTPSErrors=true. Формулировка
уточнена: старый issuer-only suite НЕ является TLS доказательством; F18 actual
full-stack с отдельной CA/leaf не отключает проверки. Исторический F17 report
не переписан. Нет утверждения «synthetic issuer = real IdP».

Первый запуск новых tests: 36passed/1failed. Тест предполагал, что каждый entry
report является network probe, но добавленный metadata-object guard также
создаёт check. Исправлен test на явный requests=0 и bounded diagnostic, а не
ослаблено origin enforcement. Повторно 37passed/0failed/0skipped. Затем suite
расширен readiness503/DevelopmentLocal/stalled-body controls: 40passed/0failed/
0skipped. Deadline проверяется по cancellation outcome без timing assertions.

## Gate B: точные блокеры / разрешения

1. Реальный staging HTTPS origin/hostname/certificate chain, deployment owner,
   private API/PG network и способ operator access.
2. Exact issuer/discovery, registered HTTPS callback, confidential client,
   API audience/scopes, client_secret_post/ID RS256/access JWT capability.
3. Secrets в approved secret manager/runtime, не chat/Git; эффективный API
   Production/JwtBearer/authority/audience/connection и worker=false.
4. Два различных authorized real test accounts с устойчивым sub; interactive
   credentials/MFA остаются у оператора, не извлекаются из cookies.
5. Отдельное точное разрешение GET probes, sign-in, harmless staging run POST,
   deployment/migrations/restart/outage/rotation/cleanup если они нужны.

Подробный пошаговый plan/commands/status matrix:
`docs/frontend/real-deployment-verification.md`. Owner Queued run:
details/progress200, report409, quantitative200empty, provenance200; foreign404
на всех UUID reads, history без чужого run. Missing report не auth failure.
Нет synthetic science seed в production. Worker отключён, live science не идёт.

Browser plan проверяет TLS chain/redirect/Host, реальный code grant/cookie/BFF/
API/PG, independent token validation, A/B isolation, logout/expiry/switch/cache,
multi-tab/back, 390px, approval-scoped restart/outage. Cookies/token/subject/client
secret/DB connection не попадают в screenshots/logs/trace/storageState/HAR.
Реальную подпись/issuer/aud нельзя объявить verified только по metadata.

## Results: prepared vs real

Локально выполнены restore/build (0 warnings/errors), full backend regression:
Domain45/Application289/Infrastructure110/Integration52 passed, 0 failed;
103 Integration skipped из-за отсутствующего Docker Linux pipe. Итого496passed.
Frontend frozen install, API40/Web99 unit tests, lint/typecheck, preflight40 и
security5 passed. UI/desktop unit cases отсутствуют и не выдаются за coverage.
Compose config passed; docker info failed на unavailable daemon. Browser/image
и настоящий PG verification нового F19 commit остаются задачей strict CI,
не заявляются локально passed. Локальный внешний browser smoke NOT RUN.

| Boundary | F19 current result |
| --- | --- |
| Offline preflight controls | 40 passed, 0 failed/skipped |
| Actual workspace configuration | NOT READY; no network |
| Production dependency audit | 0 all severities |
| Full dependency audit | 1 HIGH dev-only braces; no suppression |
| Security gate controls | 5 passed |
| Real external IdP/login/two users | NOT RUN |
| Real external HTTPS/certificate | NOT RUN |
| Real deployment JWT issuer/aud/subject | NOT VERIFIED |
| Real key rollover | NOT VERIFIED |
| Real deployed API/PG/restart/owner reads | NOT RUN |
| Live science/native Tauri | NOT RUN |
| Final deterministic CI/regression | pending |

F18 actual local synthetic HTTPS/API/PG test не переименован в external deployment.
Новый CI должен сохранить real PG/Testcontainers103 без required skips,
backend599, frontend API40/web99, security5, browser15/17/17, OpenAPI/EF/Compose,
45 populated responsive scenarios и scientific regressions. Числа до нового CI
остаются ожиданием. Offline preflight40 добавлен отдельным CI step без secrets.

## Limitations и следующий шаг

Gate A не отвечает «два actual users уже безопасно работают на реальном сервере».
Нет доказательства реального issuer/audience/token-size compatibility, TLS,
proxy/firewall, signing-key rollover, health/ownership/restart в deployment.
Stateless replay до expiry, no refresh/revocation/key rotation, best-effort tab
cleanup, script CSP/XSS/quotas и dev-only braces остаются отдельными рисками.
Научные алгоритмы/F15.1/F15.2/F16/M17-M24/leases/schema не меняются.

Ровно один следующий milestone: F19 Gate B execution в предоставленном и
явно разрешённом staging HTTPS/real IdP окружении с двумя аккаунтами. Он не
начат. После зелёного deterministic CI допустим только PREPARED - AWAITING
OPERATOR CONFIGURATION, НЕ COMPLETE - REAL DEPLOYMENT VERIFIED.
