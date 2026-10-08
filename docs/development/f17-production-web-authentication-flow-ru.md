# F17: production web authentication flow

Дата: 2026-10-08. **F17 COMPLETE - DETERMINISTIC**.
Реализация, локальные проверки и strict Linux CI завершены успешно.
Реальный внешний OIDC IdP и live scientific workflow: **NOT RUN**.

## Исходное состояние

Фактически проверен HEAD `0f273cb1064e7c48361ebf37b96927abbcc8dd2b`, main,
upstream origin/main, exact remote `https://github.com/DanDan919/MedResearch.git`.
На старте clean; log30/diff-check просмотрены. F16 CI 37752525361 был success.
Научные алгоритмы, extraction/evaluation/synthesis/grounding, M17-M24,
domain/persistence и миграции в F17 не меняются.

## Почему одного JWT API было недостаточно

Authentication отвечает «кто пришёл», authorization отвечает «можно ли этому
пользователю читать этот ResearchRun». API уже умел проверять JWT и owner, но
обычный браузер не умел получать подходящий access token и завершать сессию.
Login page без code exchange и owner-проверки не закрыла бы этот пробел.

Теперь Browser -> HTTPS Next.js -> BFF -> Bearer access JWT -> ASP.NET -> EF/PG.
OIDC issuer остаётся внешним, конфигурируемым сервисом. Production не содержит
собственного issuer/password store/test bypass. Отдельный synthetic issuer
в тестах не является продуктовой возможностью или production настройкой по умолчанию.

## Библиотеки и протокол

Проверены актуальные официальные README/API/security docs и npm versions до
выбора: openid-client 6.8.8, iron-session 9.0.1, jose 6.2.12. MIT; Node 24
совместим с требованиями. selfsigned 5.5.0 только dev fixture; server-only
защищает server imports. Existing lock versions сохранены, Next 16.3.6/React19.3.

openid-client выполняет discovery, code grant, S256 PKCE, state, nonce и
проверку ID-token подписи (enableNonRepudiationChecks). Это не самописный OAuth.
PKCE связывает code с секретным verifier в sealed flow cookie; state связывает
callback с начатой попыткой, nonce с ID token. Callback URI фиксирован оператором.
Flow cookie живёт пять минут; callback уничтожает его даже при ошибке.

ID token означает вход в web client, а не доступ к MedResearch API. jose отдельно
проверяет access JWT: asymmetric signature, exact issuer, API audience, exp,
совпадение sub с ID token; client audience и ID-token-as-access отвергаются.
Backend сохраняет независимый JwtBearer handler и свою discovery/key rotation.
Принятый здесь generic профиль: confidential client_secret_post, RS256 ID,
RS256/PS256/ES256 access JWT. Не обещается совместимость с opaque/basic-only IdP.

Официальные источники: [openid-client](https://github.com/panva/openid-client),
[grant API](https://github.com/panva/openid-client/blob/main/docs/functions/authorizationCodeGrant.md),
[iron-session v9](https://github.com/vvo/iron-session/tree/v9.0.1),
[jose](https://github.com/panva/jose),
[Next authentication](https://nextjs.org/docs/app/guides/authentication),
[Next Proxy](https://nextjs.org/docs/app/api-reference/file-conventions/proxy).

## Session и token custody

В encrypted/authenticated HttpOnly cookie находятся issuer/sub/sessionId/display,
access JWT и строгий expiresAt. Browser JS не получает token: нет localStorage,
sessionStorage, IndexedDB, props, public DTO или URL с access/refresh token.
Стандартный callback несёт только одноразовый authorization code/state.
Public session: authenticated, mode, opaque sessionId, displayName, expiresAt.
Cookie: Secure/HttpOnly/SameSite=Lax/__Host-/Path=/, без Domain.

Сессия заканчивается не позже JWT или configured maximum (default 3600sec,
60..3600). Flow 300sec; provider timeout 10sec (1..30). Token <=3000 chars,
cookie <=4KB; encryption overhead означает, что крупный token может быть отклонён.
Refresh не запрашивается и не сохраняется; offline_access запрещён. После expiry
новый login. Нет мнимого refresh success/failure: feature намеренно отсутствует.

Logout удаляет app/flow cookie, query cache и переводит на login. Не уничтожает
IdP session и не отзывает уже скопированные credentials глобально. Stateless
cookie replay возможен до expiry. Cookie secret replacement разлогинивает всех;
graceful key rotation интерфейс пока не добавлен.

## Next.js и frontend transport

Routes: /login, POST /api/auth/login, GET callback/session, POST logout.
Proxy защищает dashboard, new/history/details/report/evidence/quantitative,
studies/settings; он проверяет только sealed session/expiry. Настоящая data
проверка в BFF. SDK сохраняет typed contracts/Zod, base /api/backend;
browser getAccessToken не используется. Desktop transport не переделан.

BFF разрешает только GET health/ready, research list/details/progress/report/
quantitative/provenance и POST create. Upstream origin фиксирован server env.
Browser Authorization, X-Owner-Id/X-User-Id/forwarded identity и Cookie не
пересылаются. Bearer берётся только из private session. Redirect upstream
отклоняется; return path bounded/allowlisted, Location creation переписывается
без чужого host. Body input16KB/output10MB, OIDC1MB; deadline/cancellation.
Private/no-store во всех auth/data responses, нет общего user cache.

CSRF: exact configured Host/Origin и безопасный Sec-Fetch-Site для mutation.
SameSite не единственная защита. Origin:null не принимается. Первый browser
test обнаружил, что no-referrer suppresses Origin у native POST form:
[MDN](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Referrer-Policy).
Политику сохранили; login/logout используют same-origin fetch и явный redirect.
Для этой UX нужен JS. Callback защищается library state/PKCE/nonce, не Origin
cross-site GET. CORS backend не ослаблен, browser BFF same-origin.

401: no session/expired/invalid или backend rejected, не «пустая история».
503: IdP/API unavailable. 403/404/409/429/500 сохраняются без raw upstream body,
secret, internal hostname или stack. Logging только operation/category,
upstream получает correlation ID; научные payloads не добавлены в логи.

## Cache, expiry и смена пользователя

Каждый opaque sessionId создаёт отдельный QueryClient. Старый cache отменяется
и очищается целиком: history/details/report/provenance/quantitative. 401 и
успешный logout делают то же. Login switch сначала удаляет старую server cookie.
BroadcastChannel сообщает другим вкладкам, focus/20sec polling перепроверяют
session; pageshow persisted скрывает workspace до перепроверки.
Dormant tab может хранить ранее отрисованные данные до события/focus/poll,
но новые запросы всё равно проходят BFF/API. Мгновенное global erase не заявлено.

## ASP.NET / ownership

Production JWT: issuer/aud/signature/lifetime, MapInboundClaims=false, sub
нормализован trim и <=200; email/name/headers не определяют owner. Один
configured issuer; переход к multi-issuer потребует collision-safe identity
design. Existing OwnerSubjectId root и legacy-unowned не мигрировались.
ClockSkew framework unchanged =5min; BFF exp stricter zero. Missing JWT 401;
valid JWT без bounded sub 403; foreign/nonexistent resource одинаково 404.
Разрешения не заменены «запрос пришёл из Next.js». API OpenAPI/scientific SDK
contract не менялся; BFF transport отдельно описан в deployment guide.

## Threat / verification matrix

| Угроза / invariant | Проверка / граница |
|---|---|
| Missing/wrong signature/issuer/audience/expiry/sub | 10 actual JWT handler cases; key/metadata only synthetic |
| ID token вместо access | Separate audience/subject checks; production browser negative case + API audience rejection |
| Foreign run/data | Signed UserA/UserB + real EF/PostgreSQL test; list/details/progress/report/qty/provenance |
| Fake owner/Authorization | BFF header whitelist + production synthetic API rejection/metrics |
| CSRF/Host/open proxy/return URL | Negative config/BFF tests + real Next routes in browser |
| Invalid state/nonce/signature/callback/provider | Real synthetic HTTPS code exchange, not mocked useSession |
| Logout/expiry/account switch | Actual browser cookies, two contexts/tabs, short-lived JWT, no redirect loop |
| All scientific query caches | Unit checks five cache families; browser history isolation |
| Credentials in JS/public session | Safe DTO/props, encrypted cookie flags, storage/HTML checks, server-only imports |
| Hydration / prior science UX | Existing 14 browser regressions and production auth console/pageerror checks |

Browser fixture API is synthetic and validates signed JWT. It is NOT the ASP.NET
production API. Complementary .NET tests use actual middleware plus real PG:
no EF InMemory/SQLite fallback. New run intentionally queued; owner report=409,
foreign report=404, owner qty/provenance=200. Existing report graph/traceability
tests still exercise persisted scientific reports. This distinguishes actual
test coverage from invented end-to-end deployment claims.

## Локальная проверка

- Restore/build passed, zero warnings/errors.
- Domain45/Application289/Infrastructure110 passed, no skips/failures.
- Integration36 passed,103 skipped: Docker Linux engine unavailable; total139.
- Total backend480 passed/0failed/103skipped; live suites не запущены.
- Frontend API31/web99 passed; UI/desktop packages have no unit test files,
  passWithNoTests не выдаётся за их test coverage.
- Scientific Chromium14 и production synthetic OIDC Chromium16 passed, включая
  access/ID subject mismatch, replay использованного callback и back после logout.
- Frozen install, SDK generation, typecheck, lint, production build passed.
- Desktop React/Vite build passed locally; native Tauri build NOT RUN.
- EF pending-model no changes; Compose config passed; docker info failed
  unavailable Linux pipe. Docker Desktop не ремонтировался.

## Deployment и operator checklist

Все actual variables, required/optional, HTTPS, callback, API audience, scopes,
secret policy, internal origin и local development описаны в
[authentication.md](../frontend/authentication.md). Root .env.example не запускает
Next автоматически. Production требует Next server/API/PG/real OIDC IdP,
не static export. Startup missing config fail-closed, Production DevelopmentLocal
не активируется; API health остаётся независимым от live IdP/science.

Оператор различает unconfigured/configured/deterministic-tested/real-IdP-verified.
В F17 внешний IdP **NOT RUN**, real credentials отсутствуют/не придуманы.
Production domain/client/secret и trusted CA должны прийти от реального deployment.
Подтвердить реальные два аккаунта, code/access aud, ownership всех read routes,
logout/expiry/switch, issuer key rollover, cookies/TLS/Host и absence token leaks.

## Ответы на финальные вопросы 83–95

JS не читает access JWT; UserB/forged owner headers не дают UserA science;
ID token не заменяет audience-scoped access; Production DevelopmentLocal закрыт;
BFF не arbitrary proxy; expired cookie/JWT не авторизует запросы. Logout/switch
очищают app caches, с указанной bounded multi-tab оговоркой. API JWT независим.
Проверен только synthetic issuer, не внешний production IdP. Научные алгоритмы
не изменены. F17 COMPLETE - DETERMINISTIC подтверждён strict green CI ниже,
но не означает проверенный production deployment или отсутствие всех advisories.

Единственный рекомендуемый следующий milestone: F18 Product/Deployment
Readiness: сначала scoped dependency advisory remediation, затем реальный
OIDC/HTTPS Next/BFF/API deployment smoke, issuer/audience/key rollover и runtime
OpenAPI drift gate. Это основано
на remaining external deployment boundary/curated snapshot, не повод менять
scientific algorithms. Следующий milestone автоматически не начат.

## Финальный CI и Git

### Проверка зависимостей и diff

`pnpm audit --prod` реально выполнен: 7 advisories (2 high/4 moderate/1 low),
0 critical. Registry не перечислил добавленные openid-client/iron-session/jose;
это не универсальная гарантия отсутствия уязвимостей. Existing Next16.3.6 и
source-map-js затронуты version-based advisories. Подтверждены первичные страницы
[Next Image SSRF](https://github.com/vercel/next.js/security/advisories/GHSA-cjq9-62q9-8jv4)
и [source-map-js DoS](https://github.com/7rulnik/source-map-js/security/advisories/GHSA-68fv-2mgg-jv7q).
Next advisory указывает patch16.3.8 и отсутствие этого Image SSRF при отсутствии
remotePatterns; current next.config их не содержит. Это не clearance остальных
cache/metadata/dev advisories/source-map-js DoS. Dependency upgrade не выполнен
в F17. F18 должен начать с scoped advisory remediation/regression до deployment.

Проверены auth dependencies/cookie/callback/CSRF/Host/redirect/cache/error/logging
и server/client boundaries. Нет production credentials, keys/CA генерируются
только test process. Научные src/migrations/SDK schemas не изменены.

### Фактически проверенный GitHub Actions результат

Production feature commit: `d7ec8357dcd13f0f90f56183c696dc967678ccb1`,
`feat: add production OIDC web session and BFF`.
[CI 37788883185](https://github.com/DanDan919/MedResearch/actions/runs/37788883185):
completed/success; оба jobs Frontend и Build and test success.
Runner ubuntu-latest, .NET 10.0.x, Node 24.x. Docker info success.

Фактические backend TRX counters прочитаны через GitHub check annotations:

| Suite | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| Domain | 45 | 45 | 0 | 0 |
| Application | 289 | 289 | 0 | 0 |
| Infrastructure | 110 | 110 | 0 | 0 |
| Integration | 139 | 139 | 0 | 0 |
| Total | 583 | 583 | 0 | 0 |

В Integration входят 103 Docker/PostgreSQL cases и 36 non-DB cases. Все 103
обязательных DB tests реально выполнены: strict fixture не допускает Docker
fallback/skip; Integration counters executed=total, skipped=0. Новый signed-JWT
owner test, прежние migrations/leases/fencing/traceability и scientific
F15.1/F15.2/F16/M17-M24 regressions зелёные. EF pending-model и Compose success.

Frontend frozen install/generation drift/lint/typecheck/unit/build success;
оба Chromium suites success: scientific regression (14 cases) и production
synthetic OIDC/BFF (16 cases). Unit suites соответствуют локально проверенным
API31/web99; CI job шаги success. Анонимное скачивание полных frontend logs
через REST вернуло 403: отдельные frontend counters не выдаются за прочитанные
TRX annotations. Desktop React/Vite CI build success; native Tauri NOT RUN.
Skipped conditional upload-on-failure step не является skipped test.

В CI нет external IdP/science credentials, live OpenAI/PubMed/Europe PMC calls.
Synthetic HTTPS issuer использует ephemeral test keys/CA; настоящее production
Next.js code exchange/BFF проверяется, но external identity deployment нет.
Actions v4 выдали existing non-blocking Node20 deprecation warning. Установка
Chromium заняла около семи минут, затем suite завершился успешно; workflow
не менялся для обхода проверки, CI-fix commits не потребовались.

Feature push в exact origin/main successful; feature tree clean и diff-check
passed. Эта завершающая запись отправляется отдельным documentation-only
commit; его hash, повторный CI и окончательный clean status сообщаются в handoff,
без самоссылочного обещания hash внутри того же commit.

Local preview `http://127.0.0.1:3018/login` запущен отдельно на loopback без
IdP/secrets: HTTP200, accurate unconfigured state, protected data fail closed.
Existing server на 3000 не остановлен. Preview не является real OIDC validation.
