// @vitest-environment node
import { describe, expect, it, vi } from "vitest";
import { sealData } from "iron-session";
import { readAuthConfig, safeReturnPath, trustedRequest } from "./config";
import { forwardBackend, allowedUpstreamPath } from "./bff";
import { AccessTokenFailure } from "./oidc";
import { publicSession, readPrivateSession, savePrivateSession, sessionOptions, type OidcConfig, type PrivateSession } from "./session";
import { callback, login, logout, sessionStatus } from "./routes";
import { BodyLimitExceeded, readBoundedBody } from "./http";

const env: NodeJS.ProcessEnv = { NODE_ENV: "production", WEB_AUTH_ORIGIN: "https://web.example.org", MEDRESEARCH_API_INTERNAL_URL: "http://api:8080",
  OIDC_ISSUER: "https://identity.example.org/", OIDC_CLIENT_ID: "web-client", OIDC_CLIENT_SECRET: "synthetic-client-secret",
  OIDC_API_AUDIENCE: "research-api", OIDC_SCOPE: "openid profile research", WEB_SESSION_SECRET: "synthetic-session-password-at-least-32-characters" };
const config = readAuthConfig(env) as OidcConfig;
const data: PrivateSession = { version: 1, issuer: config.issuer, subject: "UserA", sessionId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
  displayName: "User A", accessToken: "synthetic-private-token", expiresAt: Date.now() + 60_000 };
function request(path = "/api/backend/api/research", method = "GET", headers: Record<string, string> = {}, body?: string) {
  return new Request(config.webOrigin + path, { method, headers: { host: "web.example.org", ...headers }, body });
}
const dependencies = () => ({ config, readSession: vi.fn(async () => data), verifyToken: vi.fn(async () => ({ subject: data.subject, expiresAt: data.expiresAt })),
  fetch: vi.fn<typeof fetch>(async () => Response.json({ items: [] })) });

describe("web authentication configuration", () => {
  it("preserves exact issuer and distinct API audience", () => { expect(config.mode).toBe("oidc"); expect(config.issuer).toBe(env.OIDC_ISSUER); });
  it.each(["OIDC_CLIENT_ID", "OIDC_CLIENT_SECRET", "OIDC_API_AUDIENCE", "OIDC_ISSUER", "OIDC_SCOPE", "WEB_SESSION_SECRET", "WEB_AUTH_ORIGIN", "MEDRESEARCH_API_INTERNAL_URL"])("fails closed without %s", key => {
    expect(readAuthConfig({ ...env, [key]: "" }).mode).toBe("unavailable");
  });
  it.each([{ WEB_AUTH_MODE: "DevelopmentLocal" }, { OIDC_API_AUDIENCE: "web-client" }, { WEB_AUTH_ORIGIN: "http://web.example.org" },
    { OIDC_ISSUER: "http://identity.example.org" }, { OIDC_SCOPE: "openid offline_access" }, { WEB_SESSION_MAX_AGE_SECONDS: "0" },
    { WEB_AUTH_TIMEOUT_SECONDS: "31" }, { MEDRESEARCH_API_INTERNAL_URL: "http://api:8080/path" }])("rejects unsafe settings %j", override => {
    expect(readAuthConfig({ ...env, ...override }).mode).toBe("unavailable");
  });
  it("DevelopmentLocal exists only in actual development", () => {
    expect(readAuthConfig({ NODE_ENV: "development", WEB_AUTH_MODE: "DevelopmentLocal" }).mode).toBe("development-local");
    expect(readAuthConfig({ NODE_ENV: "test", WEB_AUTH_MODE: "DevelopmentLocal" }).mode).toBe("unavailable");
  });
  it.each(["https://evil.example", "//evil.example", "/\\evil", "/%2f%2fevil", "/%252fevil", "/research/../api/auth/login", "/login", "/research#evil"])("rejects return path %s", path => {
    expect(safeReturnPath(path)).toBe("/");
  });
  it("allows only internal workspace returns", () => { expect(safeReturnPath("/research?page=2")).toBe("/research?page=2"); });
  it("requires exact Host and Origin, not forwarded headers", () => {
    expect(trustedRequest(request("/api/auth/logout", "POST", { origin: config.webOrigin }), config, true)).toBe(true);
    expect(trustedRequest(request("/api/auth/logout", "POST", { origin: "https://evil.example", "x-forwarded-host": "web.example.org" }), config, true)).toBe(false);
    expect(trustedRequest(request("/api/auth/logout", "POST", { origin: config.webOrigin, "sec-fetch-site": "cross-site" }), config, true)).toBe(false);
  });
});

describe("encrypted session", () => {
  it("strictly checks expiry, issuer, corrupt and absent cookies", async () => {
    const cookie = await sealData(data, { password: config.sessionSecret, ttl: 60 });
    expect(await readPrivateSession(cookie, config)).toEqual(data);
    expect(await readPrivateSession(cookie, config, data.expiresAt)).toBeNull();
    expect(await readPrivateSession(cookie, { ...config, issuer: "https://other.example.org/" })).toBeNull();
    expect(await readPrivateSession("broken", config)).toBeNull();
    expect(await readPrivateSession(undefined, config)).toBeNull();
    expect(await readPrivateSession("x".repeat(4097), config)).toBeNull();
  });
  it("public session never includes tokens, issuer or subject", () => {
    const output = JSON.stringify(publicSession(config, data));
    for (const secret of [data.accessToken, data.subject, config.clientSecret, config.sessionSecret, config.issuer]) expect(output.includes(secret)).toBe(false);
  });
  it("sets host-only Secure HttpOnly SameSite cookie and logout destroys it", async () => {
    const response = new Response();
    await savePrivateSession(request(), response, config, data);
    const header = response.headers.get("set-cookie")!;
    expect(header).toContain("__Host-medresearch-session=");
    expect(header).toContain("HttpOnly"); expect(header).toContain("Secure"); expect(header).toContain("SameSite=Lax");
    expect(header).not.toContain("Domain="); expect(header).not.toContain(data.accessToken);
    const result = await logout(request("/api/auth/logout", "POST", { origin: config.webOrigin, cookie: header.split(";")[0] }), config);
    expect(result.status).toBe(303); expect(result.headers.get("set-cookie")).toContain("Max-Age=0");
    expect(sessionOptions(config).cookieOptions?.secure).toBe(true);
  });
  it("anonymous session status and invalid callback disclose no credentials", async () => {
    const status = await sessionStatus(request("/api/auth/session"), config);
    expect((await status.json()).authenticated).toBe(false);
    const result = await callback(request("/api/auth/callback?code=bogus&state=bogus"), config);
    expect(result.headers.get("location")).toBe(config.webOrigin + "/login?reason=authentication-failed");
  });
  it("login and logout reject missing/cross-site Origin", async () => {
    expect((await login(request("/api/auth/login", "POST"), config)).status).toBe(403);
    expect((await logout(request("/api/auth/logout", "POST", { origin: "https://evil.example" }), config)).status).toBe(403);
  });
});

describe("BFF independently authorizes and constrains transport", () => {
  it("rejects anonymous session before upstream, even with forged Authorization", async () => {
    const dep = dependencies(); dep.readSession.mockResolvedValue(null as unknown as PrivateSession);
    const result = await forwardBackend(request(undefined, "GET", { authorization: "Bearer forged", "x-user-id": "UserA" }), dep);
    expect(result.status).toBe(401); expect(dep.fetch).not.toHaveBeenCalled();
  });
  it("strips all browser identity/cookies and attaches only private access token", async () => {
    const dep = dependencies();
    const result = await forwardBackend(request(undefined, "GET", { authorization: "Bearer forged", "x-owner-id": "UserB", cookie: "browser=secret" }), dep);
    expect(result.status).toBe(200);
    const [url, init] = dep.fetch.mock.calls[0]; expect(url).toBe("http://api:8080/api/research");
    expect(init?.headers).toEqual({ Accept: "application/json", "X-Correlation-Id": expect.any(String), Authorization: "Bearer " + data.accessToken });
    expect(init?.redirect).toBe("manual"); expect(init?.cache).toBe("no-store"); expect(result.headers.get("cache-control")).toContain("private, no-store");
  });
  it.each(["/api/backend/https://evil.example", "/api/backend//evil.example", "/api/backend/api/research/%2e%2e/health", "/api/backend/api/research?url=https://evil.example",
    "/api/backend/api/research?owner=UserB", "/api/backend/api/research?page=1&page=2", "/api/backend/api/research?pageSize=999", "/api/backend/api/research?status=Anything"])("rejects proxy path %s", path => {
    expect(allowedUpstreamPath(new URL(config.webOrigin + path), "GET")).toBeNull();
  });
  it("rejects unallowed method and mutation CSRF", async () => {
    expect(allowedUpstreamPath(new URL(config.webOrigin + "/api/backend/api/research"), "DELETE")).toBeNull();
    const dep = dependencies(); const result = await forwardBackend(request(undefined, "POST", { origin: "https://evil.example" }), dep);
    expect(result.status).toBe(403); expect(dep.fetch).not.toHaveBeenCalled();
  });
  it.each([400, 401, 403, 404, 409, 429, 500, 503])("preserves HTTP %i without upstream error disclosure", async code => {
    const dep = dependencies(); dep.fetch.mockResolvedValue(new Response("private stack/secret", { status: code }));
    const response = await forwardBackend(request(), dep);
    expect(response.status).toBe(code); expect(await response.text()).not.toContain("private stack/secret");
  });
  it("separates invalid token and provider outage", async () => {
    const dep = dependencies(); dep.verifyToken.mockRejectedValue(new AccessTokenFailure("invalid-token"));
    expect((await forwardBackend(request(), dep)).status).toBe(401);
    dep.verifyToken.mockRejectedValue(new AccessTokenFailure("provider-unavailable"));
    expect((await forwardBackend(request(), dep)).status).toBe(503); expect(dep.fetch).not.toHaveBeenCalled();
  });
  it("rejects upstream redirects and network failures", async () => {
    const dep = dependencies(); dep.fetch.mockResolvedValue(new Response(null, { status: 302, headers: { location: "https://evil.example" } }));
    expect((await forwardBackend(request(), dep)).status).toBe(502);
    dep.fetch.mockRejectedValue(new Error("private-host/secret"));
    const response = await forwardBackend(request(), dep); expect(response.status).toBe(503); expect(await response.text()).not.toContain("private-host");
  });
  it.each(["{", JSON.stringify({ question: "x", owner: "UserB" }), JSON.stringify({ question: 3 })])("rejects invalid POST body", async body => {
    const dep = dependencies(); expect((await forwardBackend(request(undefined, "POST", { origin: config.webOrigin, "content-type": "application/json" }, body), dep)).status).toBe(400);
    expect(dep.fetch).not.toHaveBeenCalled();
  });
  it("caps POST bodies and successful upstream bodies", async () => {
    const dep = dependencies(); expect((await forwardBackend(request(undefined, "POST", { origin: config.webOrigin, "content-type": "application/json" }, "x".repeat(16_385)), dep)).status).toBe(413);
    dep.fetch.mockResolvedValue(new Response("x", { headers: { "content-length": "10000001" } }));
    expect((await forwardBackend(request(), dep)).status).toBe(503);
  });
  it("health is limited, public and has no bearer token", async () => {
    const dep = dependencies(); const response = await forwardBackend(request("/api/backend/health/ready"), dep);
    expect(response.status).toBe(200); expect(dep.readSession).not.toHaveBeenCalled();
    expect(dep.fetch.mock.calls[0][1]?.headers).not.toHaveProperty("Authorization");
  });
  it("unconfigured production fails closed", async () => { expect((await forwardBackend(request(), { config: { mode: "unavailable" } })).status).toBe(503); });
  it("rewrites safe creation Location without forwarding unsafe or malformed destinations", async () => {
    const dep = dependencies(); const path = "/api/research/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    for (const location of [path, "https://evil.example" + path, "http://["]) {
      dep.fetch.mockResolvedValue(Response.json({ researchRunId: data.sessionId, status: "Queued" }, { status: 201, headers: { location } }));
      const response = await forwardBackend(request(undefined, "POST", { origin: config.webOrigin, "content-type": "application/json" }, JSON.stringify({ question: "A bounded question" })), dep);
      expect(response.status).toBe(201); expect(response.headers.get("location")).toBe(location === path ? "/api/backend" + path : null);
    }
  });
});

describe("body limits", () => {
  it("accepts exact cap, rejects overflow and propagates cancellation", async () => {
    expect((await readBoundedBody(new Response("abcd"), 4)).length).toBe(4);
    await expect(readBoundedBody(new Response("abcde"), 4)).rejects.toBeInstanceOf(BodyLimitExceeded);
    const controller = new AbortController(); controller.abort();
    await expect(readBoundedBody(new Response("abcd"), 4, controller.signal)).rejects.toThrow();
  });
});
