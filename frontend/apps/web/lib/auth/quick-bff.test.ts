// @vitest-environment node
import { describe, expect, it, vi } from "vitest";
import { readAuthConfig, safeReturnPath } from "./config";
import { forwardBackend, allowedUpstreamPath } from "./bff";
import type { OidcConfig, PrivateSession } from "./session";
import { quickDetails, quickHistory, quickRunId } from "../../../../packages/api/test/fixtures/quick";

const config = readAuthConfig({ NODE_ENV: "production", WEB_AUTH_ORIGIN: "https://web.example.org", MEDRESEARCH_API_INTERNAL_URL: "http://api:8080",
  OIDC_ISSUER: "https://identity.example.org/", OIDC_CLIENT_ID: "web-client", OIDC_CLIENT_SECRET: "synthetic-client-secret",
  OIDC_API_AUDIENCE: "research-api", OIDC_SCOPE: "openid profile research", WEB_SESSION_SECRET: "synthetic-session-password-at-least-32-characters" }) as OidcConfig;
const data: PrivateSession = { version: 1, issuer: config.issuer, subject: "UserA", sessionId: quickRunId, displayName: "User A",
  accessToken: "synthetic-private-A", expiresAt: Date.now() + 60000 };
const base = "/api/backend/api/quick-research";
function request(path = `${base}/${quickRunId}`, method = "GET", headers = {}) {
  return new Request(config.webOrigin + path, { method, headers: { host: "web.example.org", ...headers } });
}
function deps() {
  return { config, readSession: vi.fn(async () => data), verifyToken: vi.fn(async () => ({ subject: data.subject, expiresAt: data.expiresAt })),
    fetch: vi.fn<typeof fetch>(async () => Response.json(quickDetails())) };
}

describe("read-only Quick BFF", () => {
  it.each([base, `${base}?page=1&pageSize=20`, `${base}/${quickRunId}`])("allows exact GET %s", path => {
    expect(allowedUpstreamPath(new URL(config.webOrigin + path), "GET")).toBe(path.replace("/api/backend", ""));
  });
  it.each([[`${base}/${quickRunId}`, "POST"], [base, "DELETE"], [`${base}/${quickRunId}`, "PATCH"], [`${base}/${quickRunId}/report`, "GET"],
    [`${base}?owner=UserB`, "GET"], [`${base}?page=1&page=2`, "GET"], [`${base}?page=0`, "GET"], [`${base}?pageSize=51`, "GET"],
    [`${base}/${quickRunId}?owner=UserB`, "GET"], [`${base}/not-id`, "GET"]])("rejects %s %s", (path, method) => {
    expect(allowedUpstreamPath(new URL(config.webOrigin + path), method)).toBeNull();
  });
  it("uses only the verified private bearer and returns private no-store", async () => {
    const d = deps();
    const response = await forwardBackend(request(undefined, "GET", { authorization: "Bearer forged", cookie: "browser=secret", "x-owner-id": "UserB" }), d);
    expect(response.status).toBe(200);
    const headers = new Headers(d.fetch.mock.calls[0][1]?.headers);
    expect(headers.get("authorization")).toBe(`Bearer ${data.accessToken}`);
    expect(headers.has("x-owner-id")).toBe(false); expect(headers.has("cookie")).toBe(false);
    expect(d.fetch.mock.calls[0][1]?.cache).toBe("no-store");
    expect(response.headers.get("cache-control")).toContain("private, no-store");
    expect(JSON.stringify(await response.json())).not.toContain(data.accessToken);
  });
  it("User B receives the same private 404 as an unknown run", async () => {
    const d = deps();
    d.readSession.mockResolvedValue({ ...data, subject: "UserB", accessToken: "synthetic-private-B" });
    d.fetch.mockImplementation(async (_url, init) => new Headers(init?.headers).get("authorization") === "Bearer synthetic-private-A"
      ? Response.json(quickDetails()) : Response.json({ title: "private upstream diagnostics" }, { status: 404 }));
    const response = await forwardBackend(request(undefined, "GET", { "x-owner-id": "UserA", authorization: "Bearer synthetic-private-A" }), d);
    expect(response.status).toBe(404); expect(await response.text()).not.toContain("Synthetic question");
    d.fetch.mockResolvedValue(Response.json({ ...quickHistory(), items: [], totalCount: 0 }));
    expect((await (await forwardBackend(request(base), d)).json()).items).toEqual([]);
  });
  it("rejects anonymous, cross-origin and cross-site GET before API", async () => {
    const d = deps();
    for (const headers of [{ origin: "https://evil.example" }, { "sec-fetch-site": "cross-site" }, { "sec-fetch-site": "same-site" }])
      expect((await forwardBackend(request(undefined, "GET", headers), d)).status).toBe(403);
    d.readSession.mockResolvedValue(null as unknown as PrivateSession);
    expect((await forwardBackend(request(), d)).status).toBe(401);
    expect(d.fetch).not.toHaveBeenCalled();
  });
  it("supports safe login returns without allowing traversal or external redirects", () => {
    expect(safeReturnPath(`/quick-research/${quickRunId}`)).toBe(`/quick-research/${quickRunId}`);
    expect(safeReturnPath("/quick-research")).toBe("/quick-research");
    expect(safeReturnPath("/quick-research/new")).toBe("/quick-research/new");
    expect(safeReturnPath("/quick-research/../api/auth/login")).toBe("/");
    expect(safeReturnPath("//evil.example/quick-research")).toBe("/");
  });
  it("creates only through exact authenticated same-origin POST, with private bearer and no fictitious idempotency", async () => {
    const d = deps();
    d.fetch.mockResolvedValue(Response.json({}, { status: 202, headers: { location: `/api/quick-research/${quickRunId}` } }));
    const req = new Request(config.webOrigin + base, { method: "POST", headers: { host: "web.example.org", origin: config.webOrigin, "content-type": "application/json",
      authorization: "Bearer forged", "x-owner-id": "UserB", "idempotency-key": quickRunId }, body: JSON.stringify({ question: "A medical question" }) });
    const result = await forwardBackend(req, d);
    expect(result.status).toBe(202); expect(result.headers.get("location")).toBe(`${base}/${quickRunId}`);
    const headers = new Headers(d.fetch.mock.calls[0][1]?.headers);
    expect(headers.get("authorization")).toBe(`Bearer ${data.accessToken}`);
    expect(headers.has("x-owner-id")).toBe(false); expect(headers.has("idempotency-key")).toBe(false);
    expect(result.headers.get("cache-control")).toContain("private, no-store");
  });
  it.each([{ question: " " }, { question: "x".repeat(2001) }, { question: "Valid", owner: "UserB" }, { question: 123 }, []])("rejects invalid Quick create payload %# before persistence", async payload => {
    const d = deps();
    const req = new Request(config.webOrigin + base, { method: "POST", headers: { host: "web.example.org", origin: config.webOrigin, "content-type": "application/json" }, body: JSON.stringify(payload) });
    expect((await forwardBackend(req, d)).status).toBe(400); expect(d.fetch).not.toHaveBeenCalled();
  });
  it.each([undefined, "https://evil.example", "null"])("rejects POST Origin %s before upstream", async origin => {
    const d = deps(); const req = new Request(config.webOrigin + base, { method: "POST", headers: { host: "web.example.org", ...(origin ? { origin } : {}),
      "content-type": "application/json" }, body: JSON.stringify({ question: "Valid" }) });
    expect((await forwardBackend(req, d)).status).toBe(403); expect(d.fetch).not.toHaveBeenCalled();
  });
});
