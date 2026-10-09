# Real Deployment Verification: F19 Gates

This is an operator runbook, not evidence of a deployed environment. Gate A is
repository preparation. Gate B needs a real staging HTTPS origin, external OIDC
registration, PostgreSQL stack, two authorized identities and explicit approval
for the exact actions. A script flag does not authorize this agent to act.

## Approval and Prerequisites

Before Gate B, provide non-secret deployment details through the approved
operator channel: environment owner/type, HTTPS origin, exact issuer/discovery
URL, registered callback, client authentication method, ID/access signing
algorithms, API audience/scopes, private API topology and test-account availability.
Do not paste secrets, account passwords, JWTs, cookie values or account identifiers
into chat, Git, screenshots or test reports. Inject client/session/database
secrets only in the restricted deployment environment.

Record separate permission for each applicable action: resource provisioning,
DNS/TLS/proxy changes, IdP registration/users, deployment/configuration/migrations,
anonymous GET probes, interactive sign-in, test ResearchRun POST, API restart,
outage injection, signing-key rollover and cleanup. Approval for reading metadata
is not approval for a login, migration or creation. No such actions are performed
automatically by repository CI or this runbook.

Use isolated staging/test accounts and data. Keep `ResearchProcessing__Enabled=false`
in the actual API, not just a frontend option; the checked-in default is true.
Do not use development Compose for exposed deployment: it enables DevelopmentLocal,
the worker, public API/database ports and a development database password.
Confirm worker state before permitting POST. Never seed synthetic scientific
Evidence into production or submit medical/personal data for an auth test.

## Actual Client Compatibility

The existing generic adapter requires authorization code/S256 PKCE, HTTPS
discovery/JWKS, `client_secret_post`, RS256 ID tokens, and a separate signed
RS256/PS256/ES256 access JWT. The API audience must differ from the web client ID;
access `aud` must not contain the web client ID. Access and ID token `sub` must
match, be stable and bounded. Two test accounts must have distinct subjects.
Opaque tokens, basic-only clients and incompatible subject/audience issuance do
not pass merely because the IdP can display a login page. No vendor is selected
without operator-supplied options and real token compatibility evidence.

Use the exact issuer including trailing slash in both Next and API configuration.
Pairwise-subject metadata alone does not prove ID/access subject alignment.
PKCE support not advertised in metadata is inconclusive: the conservative
preflight blocks automatic clearance, but does not prove the provider lacks it.
Resolve that through documented provider capabilities and an approved real flow,
not by relaxing authentication. Metadata cannot prove access-token audience,
subject, size (3000-character cap), registered callback or secret correctness.

## Offline Configuration Preflight

Use Node 24.12+ and the frozen frontend dependencies. Inject the *effective*
Next/API environment into a restricted operator shell; the script does not load
env files automatically. For JSON/secret-manager configuration, export equivalent
effective values for this check without echoing them. From `frontend`:

```sh
pnpm deployment:preflight
pnpm deployment:test
```

The preflight imports the actual Next `readAuthConfig`, not a second validator.
It also checks Production modes, exact cross-process issuer/audience alignment,
database configuration presence, explicitly disabled worker and no Node TLS
bypass. It emits only check names/booleans/categories/statuses, never supplied
values. Exit 0 means local preflight passed, not deployment verified; exit 1
requires operator correction. Database presence is not connection-string parsing,
permissions, schema, persistence, firewall or connection verification.

Required effective values are listed in [authentication](authentication.md) and
[deployment](deployment.md): additionally `NODE_ENV=production`,
`ASPNETCORE_ENVIRONMENT=Production`, `Authentication__Mode=JwtBearer`,
`Authentication__Authority`, `Authentication__Audience`,
`ConnectionStrings__MedResearch`, `ResearchProcessing__Enabled=false`.
Generate a random session secret in approved secret management; length alone
does not establish entropy. Do not reuse synthetic credentials or fixture CAs.

CI runs only `deployment:test` with explicit synthetic env/fake HTTP. It does not
need an external IdP or actual secret and must never run network preflight.
Node's `--conditions=react-server` is used only for the operator/test executable
to import existing server-only validators; production browser guards are unchanged.

## Approved Anonymous Read-Only Probes

Only after permission for the exact destinations, use the operator-provided
origins in the following command. Angle-bracket values are placeholders, not
working deployment settings:

```sh
pnpm deployment:preflight --network-approved \
  --allow-origin=https://<web-host> \
  --allow-origin=https://<issuer-host> \
  --allow-origin=https://<jwks-host-if-different> \
  --allow-origin=http://<private-api-host>:8080
```

Every requested origin must be explicitly listed. Requests are only anonymous
GETs: discovery, public JWKS, web anonymous session, private API live/ready and
anonymous research rejection. No token exchange, credentials, cookies, POST,
redirect following, retry loop or authenticated data read. Bodies are capped at
1MB and the existing auth timeout covers transport/body reads. Metadata cannot
cause a fetch to an unapproved origin. Diagnostics discard raw bodies/errors.
HTTPS uses Node certificate/hostname verification; no `-k`, TLS bypass or fixture
trust installation. Private HTTP is permissible only on the approved trusted
internal network. Do not expose API/PG just to make this probe reachable.

Metadata checks follow [OIDC Discovery](https://openid.net/specs/openid-connect-discovery-1_0.html).
The command checks public key shape, not a real JWT signature or rollover.
Missing S256 advertisement prevents preflight clearance; verify actual support
separately as noted above. Probe success still reports real login/ownership/key
rotation as NOT RUN. Node/system custom trust stores cannot prove a public browser
certificate chain; Gate B must inspect the real browser chain separately.

## Gate B: Authorized Browser and Two Users

Use separate normal browser profiles/contexts for A and B. Each signs in through
the real issuer; never edit cookies/subjects or inject fixture tokens. Do not
save Playwright storageState, authentication traces, HARs or unredacted screenshots
of real sessions. Interactive credentials/MFA remain with the authorized operator.

1. Confirm actual staging deployment, private Next/API/PG listeners, no fixture
   issuer/control endpoints, migrated database and disabled scientific worker.
2. Inspect the real HTTPS hostname/chain/expiry in the browser; verify HTTP goes
   only to the intended HTTPS origin. Reject test/self-signed fixture trust as
   proof of public TLS. Check proxy preserves exact Host, strips client-supplied
   forwarded headers and does not cache authenticated responses.
3. Open a protected route anonymously; complete real code login for A with the
   exact registered callback. Verify no redirect loop. A successful BFF data read
   must reach the actual independently validating ASP.NET JWT API and actual PG.
4. Inspect cookie *attributes*, not values: __Host-, Secure, HttpOnly, SameSite=Lax,
   Path=/, no Domain, bounded expiry. Verify public session/DOM/storage/URL/console
   contain no access/refresh JWT. Do not upload tokens to an external decoder.
5. With specific POST permission, submit a harmless synthetic question and record
   its run ID only in the restricted staging checklist. It remains Queued because
   the worker is off. No completed report or scientific output is fabricated.
6. As A, read history/details/progress/report/quantitative/provenance using the
   browser BFF. Compare with the actual status matrix below.
7. Log in as B through the actual IdP; confirm A's run absent from B's history.
   Request the known A run via every read path. All must be 404 without owner
   disclosure. Compare a nonexistent UUID to avoid treating failure as isolation.
8. Back as A, confirm those resources still readable and the API still rejects an
   anonymous direct read with 401. Record issuer/audience/signature/expiry/subject
   alignment as pass/fail from actual validation, not raw JWT/claim dumps. A real
   login alone or a request to a synthetic backend is insufficient.
9. Logout A; protected BFF read must be 401, UI requires sign-in, scientific cache
   clears. Sign in B in the same profile and verify no cached A data appears.
   Exercise back navigation and a second tab; document dormant-rendered-data delay.
10. Observe natural bounded session/token expiry or use a separately approved
    short-lived staging policy. After expiry, old-session BFF read must fail;
    reauthentication must work without redirect loops/false empty results.
11. At 390px check login/dashboard/history/report/evidence/quantitative navigation,
    keyboard and internal scientific table/plot scrolling. For a Queued run,
    report-not-ready/empty artifacts are correct; populated scientific screens
    remain deterministic CI evidence unless real staging data is approved.
12. Only with separate permission, test API restart and retained run/ownership,
    staging API/PG outage, safe callback/audience misconfiguration and recovery.
    Do not disrupt a shared IdP. Live 200/ready 503 when PG is unavailable; BFF/API
    errors are operational failures, not empty scientific results.
13. Trigger signing-key rollover only in an explicitly approved isolated issuer.
    Otherwise record REAL KEY ROTATION NOT VERIFIED, even if discovery/JWKS works.
    Readiness/liveness never calls IdP/scientific providers to emulate this check.
14. Retain only sanitized statuses/correlation IDs, check outcomes and approved
    non-sensitive environment identifiers. Cleanup/data deletion is a separate
    approved action; do not reset the database or reassign owner IDs manually.

| BFF read path after `/api/backend` | A owns Queued run | B requests A run |
| --- | --- | --- |
| `/api/research` | 200, own run present | 200, A run absent |
| `/api/research/{id}` | 200 | 404 |
| `/api/research/{id}/progress` | 200 | 404 |
| `/api/research/{id}/report` | 409, report not ready | 404 |
| `/api/research/{id}/quantitative` | 200, empty array | 404 |
| `/api/research/{id}/provenance` | 200, actual persisted coverage | 404 |

For an approved already-completed owner run, report may be 200. Do not treat an
owner's report 409 as authentication failure. The known UUID and a random missing
UUID must not reveal existence to B. Synthetic seeded report tests remain separate.

## Evidence Limits and Handoff

Logout deletes application cookies/cache; it does not revoke copied tokens,
destroy the external IdP session or prove immediate global/tab erasure. No refresh
storage or graceful cookie-key rotation is implemented. API clock skew remains
five minutes; BFF expiry has zero skew. Real provider UX/account selection,
subject stability, JWKS cache/rotation and token size require Gate B evidence.

Gate A only may be classified PREPARED - AWAITING OPERATOR CONFIGURATION after
deterministic CI is green. Gate B requires actual recorded observations, permissions
and two real authorized identities. Live science/native Tauri are not included.
