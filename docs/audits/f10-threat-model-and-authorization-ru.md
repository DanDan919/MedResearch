# F10: аудит threat model и authorization boundary

Дата: 2026-10-04
Фактический baseline: `0256a8627346d291cf585afe4d8e244c9f49a74f`
Ветка: `main`
Репозиторий: `https://github.com/DanDan919/MedResearch.git`

## Краткий результат

До F10 research API был local-development-only: все research routes были
анонимными, а EF reads принимали только UUID запуска. Это было реальным HIGH
риском BOLA/IDOR, а не только отсутствием UI login. F10 добавляет framework
authentication, immutable ownership root и SQL-scoped reads. Научные формулы,
lease/fencing, provenance и F9 retry contracts не изменялись.

## F9 precondition

Проверено в коде: `ResearchPlanner` переиспользует эквивалентный план;
scientific search coordinator/store переиспользуют успешное execution key;
stale stage writes требуют lease owner/version; empty evidence не считается
abstract-only; `.github/workflows/ci.yml` устанавливает Chromium и запускает
Playwright. Поэтому F10 продолжен на актуальном F9 baseline.

## Assets и trust boundaries

Защищаемые user/run-scoped assets: `ResearchQuestion`, `ResearchRun`,
`ResearchPlan`, searches/discoveries, `SourceMaterial` links, Evidence,
extractions, evaluations, quantitative snapshots, reports, claims и citation
lineage. Глобальная scientific identity: canonical `Study` и reusable
`SourceMaterial` metadata; их run-specific relationships остаются защищёнными.
Также защищаются API/LLM/provider credentials и operational logs.

```text
[Browser / Tauri]
        | untrusted HTTP client
        v
[ASP.NET authentication middleware]
        | validated immutable sub
        v
[ICurrentActor + AuthenticatedUser policy]
        | owner subject scope
        v
[Application use cases]
        | SQL owner predicate
        +--> [PostgreSQL]
        +--> [Background worker: lease/version fence]
        +--> [LLM/scientific provider adapters]
```

Практические attackers: anonymous caller; authenticated User A changing a
route UUID to User B's UUID; malicious client submitting an owner ID; stale or
invalid token; a worker/application bug crossing run boundaries. TLS, identity
provider availability and infrastructure firewall configuration are deployment
responsibilities and здесь не объявляются проверенными.

## Endpoint policy

| Endpoint | Anonymous | Owner | Other authenticated user | Enforcement |
|---|---|---|---|---|
| `/health`, `/health/live`, `/health/ready` | allowed | allowed | allowed | health mapping only; no research data |
| `POST /api/research` | 401 | 201 | 201 for own new run | policy + `ICurrentActor` + persisted owner |
| `GET /api/research` | 401 | own rows only | own rows only | owner predicate before count/page |
| `GET /api/research/{id}` | 401 | 200/404 | 404 | owner-scoped store lookup |
| `GET /api/research/{id}/progress` | 401 | 200/404 | 404 | owner-scoped progress query |
| `GET /api/research/{id}/report` | 401 | 200/409/404 | 404 | owner-scoped run/report query |
| `GET /api/research/{id}/quantitative` | 401 | 200/404 | 404 | owner-scoped run/artifact query |

After authentication, a foreign resource and a nonexistent resource have the
same 404-style behavior. There are no current endpoints taking raw Evidence,
Study, SourceMaterial, or user IDs; downstream IDs returned in report/
quantitative payloads are not alternate access paths.

## Authentication decision

Production uses `Microsoft.AspNetCore.Authentication.JwtBearer`. The API
requires `Authentication:Mode=JwtBearer`, `Authority`, and `Audience`; the
framework validates issuer, audience, signature and lifetime. It preserves
`sub` as the claim type and does not decode or trust unsigned tokens manually.
The API never issues JWTs, stores passwords, or implements refresh tokens.

Development Compose uses explicit `Authentication:Mode=DevelopmentLocal`, only
under `ASPNETCORE_ENVIRONMENT=Development`, with the deterministic subject
`local-development-user`. A startup guard rejects that mode in Production.
Integration tests register a handler from the test project, allowing UserA,
UserB and anonymous clients without a live OIDC server. A valid bounded `sub`
is required by a custom authorization requirement; missing/blank/oversized
subjects fail closed.

Bearer is the intended transport. The frontend client has one optional
`getAccessToken` hook and adds `Authorization: Bearer ...` centrally; no
localStorage token persistence or cookie auth was invented. Consequently this
milestone does not claim session/refresh lifecycle or cookie CSRF protection.
Runtime OpenAPI is development-only; the checked-in frontend contract is not a
data-access control and research routes remain protected independently.

## Ownership dataflow

```text
external authenticated sub
        -> authentication middleware
        -> ICurrentActor.RequireSubjectId()
        -> CreateResearchUseCase
        -> ResearchQuestion.OwnerSubjectId
        -> ResearchRun.ResearchQuestionId
        -> owner-scoped run/progress/report/quantitative reads

worker claim -> lease owner/version -> permission to mutate this run now
```

These are separate controls. User authorization does not grant a stale worker
write permission; lease fencing remains authoritative for worker mutations.

`OwnerSubjectId` is bounded to 200 characters and treated as opaque: it is
trimmed but not lower-cased. It is immutable because F10 adds no transfer
operation. The migration backfills pre-existing rows to `legacy-unowned`, an
explicit inaccessible subject, never to the first caller.

## Findings and fixes

### HIGH: anonymous and cross-user research access

- Attack: call any research endpoint without a principal or change `{id}` to a
  known foreign UUID.
- Root cause: no authentication and `WHERE run.id = @id` only.
- Fix: JWT/local/test auth boundary, immutable question owner, owner-scoped
  Application contracts and EF joins, 404 anti-disclosure policy.
- Regression: `ResearchApiTests` anonymous rejection, owner success, cross-user
  run/list/report/quantitative tests; PostgreSQL owner-scoped read test.

### HIGH: production local-auth downgrade

- Attack: deploy with local deterministic identity enabled.
- Root cause: no previous auth mode or startup guard.
- Fix: `DevelopmentLocal` requires Development environment; default production
  mode is JWT and missing issuer/audience fails startup.
- Regression: configuration path and environment guard are explicit in
  `AuthenticationConfiguration`; production deployment must supply issuer/
  audience.

### MEDIUM: list count/existence disclosure

- Attack: infer global dataset size from `totalCount`/`totalPages`.
- Fix: ownership predicate is applied before `CountAsync`, ordering, skip and
  take; foreign/nonexistent resource responses use 404-style semantics.
- Regression: API list isolation and PostgreSQL owner-scoped query tests.

### LOW/DOCS: client error ambiguity

- Fix: API package maps 401 to `unauthorized`; web screens show
  `Authentication required`, not `not found`, `failed`, or `no evidence`.
- Regression: API client unit test and Playwright unauthenticated report test.

## Worker and scientific regression boundary

The worker has no HTTP `ClaimsPrincipal` and continues as trusted system
execution. It preserves the owner already stored on the run; stage stores still
require the lease owner/version fence. F9 plan/search retry idempotency,
same-run citation checks, SourceMaterial lineage and M17-M24 deterministic
calculations are out of the authorization path and remain unchanged.

## Explicit remaining risks

- No external identity-provider tenant is configured or live-tested here.
- No refresh-token/session lifecycle, user registration, sharing, RBAC,
  ownership transfer, quota, or per-user rate limit exists.
- Pre-F10 `legacy-unowned` rows need an explicit future migration tool.
- Authorization is application/store query enforcement, not a universal database
  row-level-security policy; direct SQL credentials remain trusted deployment
  infrastructure.
- CORS remains allowlisted and does not use `AllowAnyOrigin` with credentials,
  but TLS/proxy policy is not established by this repository.
