import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import test from "node:test";
import { configurationChecks, metadataChecks, runPreflight } from "./deployment-preflight.mjs";

const env = { NODE_ENV: "production", ASPNETCORE_ENVIRONMENT: "Production", WEB_AUTH_MODE: "Oidc",
  WEB_AUTH_ORIGIN: "https://web.example.org", MEDRESEARCH_API_INTERNAL_URL: "http://api:8080",
  OIDC_ISSUER: "https://identity.example.org/realm/", OIDC_CLIENT_ID: "synthetic-web-client",
  OIDC_CLIENT_SECRET: "synthetic-client-secret", OIDC_API_AUDIENCE: "synthetic-api", OIDC_SCOPE: "openid research",
  WEB_SESSION_SECRET: "synthetic-session-secret-at-least-32-characters", Authentication__Mode: "JwtBearer",
  Authentication__Authority: "https://identity.example.org/realm/", Authentication__Audience: "synthetic-api",
  ConnectionStrings__MedResearch: "synthetic-database-setting", ResearchProcessing__Enabled: "false" };
const metadata = { issuer: env.OIDC_ISSUER, authorization_endpoint: "https://identity.example.org/authorize",
  token_endpoint: "https://identity.example.org/token", jwks_uri: "https://identity.example.org/keys",
  response_types_supported: ["code"], grant_types_supported: ["authorization_code"],
  token_endpoint_auth_methods_supported: ["client_secret_post"], id_token_signing_alg_values_supported: ["RS256"],
  subject_types_supported: ["public"], code_challenge_methods_supported: ["S256"] };
const origins = ["https://web.example.org", "https://identity.example.org", "http://api:8080"];
const options = { networkApproved: true, origins };
const noNetwork = () => { throw new Error("Unexpected network access"); };

test("offline preflight reuses the production validator and never calls HTTP", async () => {
  const result = await runPreflight(env, {}, noNetwork);
  assert.equal(result.passed, true);
  assert.equal(result.network, "NOT RUN");
  assert.equal(result.realLogin, "NOT RUN");
  assert.equal(result.realOwnerIsolation, "NOT RUN");
});

for (const [name, override] of [
  ["missing credentials", { OIDC_CLIENT_SECRET: "" }], ["weak cookie secret", { WEB_SESSION_SECRET: "short" }],
  ["development bypass", { WEB_AUTH_MODE: "DevelopmentLocal" }], ["non-production web", { NODE_ENV: "development" }],
  ["non-production API", { ASPNETCORE_ENVIRONMENT: "Development" }], ["API bypass", { Authentication__Mode: "DevelopmentLocal" }],
  ["issuer mismatch", { Authentication__Authority: "https://other.example.org/" }], ["audience mismatch", { Authentication__Audience: "other-api" }],
  ["shared web/API audience", { OIDC_API_AUDIENCE: env.OIDC_CLIENT_ID }], ["unsafe origin", { WEB_AUTH_ORIGIN: "http://web.example.org" }],
  ["credential URL", { OIDC_ISSUER: "https://secret:password@identity.example.org" }], ["missing database", { ConnectionStrings__MedResearch: "" }],
  ["worker default", { ResearchProcessing__Enabled: undefined }], ["worker enabled", { ResearchProcessing__Enabled: "true" }],
  ["TLS bypass", { NODE_TLS_REJECT_UNAUTHORIZED: "0" }]
]) test(`configuration rejects ${name} without network or sensitive diagnostics`, async () => {
  const result = await runPreflight({ ...env, ...override }, options, noNetwork);
  assert.equal(result.passed, false);
  assert.equal(result.network, "NOT RUN");
  for (const value of [env.OIDC_CLIENT_SECRET, env.WEB_SESSION_SECRET, env.ConnectionStrings__MedResearch, "secret:password"])
    assert.equal(JSON.stringify(result).includes(value), false);
});

test("the actual config validator retains exact issuer and bounded settings", () => {
  assert.equal(configurationChecks(env).config.issuer, env.OIDC_ISSUER);
  assert.equal(configurationChecks({ ...env, WEB_AUTH_TIMEOUT_SECONDS: "31" }).config.mode, "unavailable");
});

test("invalid explicit origins are rejected without any request", async () => {
  const result = await runPreflight(env, { networkApproved: true, origins: ["https://user:password@web.example.org"] }, noNetwork);
  assert.equal(result.network, "REJECTED");
  assert.equal(result.passed, false);
});

test("network opt-in without exact approved origins does not issue a request", async () => {
  let requests = 0;
  const result = await runPreflight(env, { networkApproved: true }, () => { requests++; throw new Error("Unexpected network access"); });
  assert.equal(result.passed, false);
  assert.equal(requests, 0);
  assert.equal(result.probes.some(probe => probe.category === "origin-not-approved"), true);
});

for (const [name, override] of [
  ["wrong issuer", { issuer: "https://other.example.org" }], ["HTTP endpoint", { token_endpoint: "http://identity.example.org/token" }],
  ["credentials in endpoint", { jwks_uri: "https://user:password@identity.example.org/keys" }],
  ["basic-only client", { token_endpoint_auth_methods_supported: ["client_secret_basic"] }],
  ["missing client authentication", { token_endpoint_auth_methods_supported: undefined }],
  ["unsupported ID signing", { id_token_signing_alg_values_supported: ["HS256"] }],
  ["no code flow", { response_types_supported: ["id_token"] }], ["no S256 advertisement", { code_challenge_methods_supported: undefined }]
]) test(`metadata preflight rejects ${name}, not real token verification`, () => {
  assert.equal(metadataChecks({ ...metadata, ...override }, env.OIDC_ISSUER).every(check => check.passed), false);
});

function fixture(overrides = {}) {
  const calls = [];
  return { calls, fetcher: async (address, init) => {
    calls.push({ address, init });
    if (address.endsWith("/.well-known/openid-configuration")) return overrides.discovery?.() ?? Response.json(overrides.metadata ?? metadata);
    if (address.endsWith("/keys")) return Response.json(overrides.keys ?? { keys: [{ kty: "RSA", n: "synthetic-modulus", e: "AQAB", use: "sig" }] });
    if (address.endsWith("/api/auth/session")) return Response.json(overrides.session ?? { authenticated: false, mode: "oidc" });
    if (address.endsWith("/api/research")) return new Response(null, { status: 401 });
    if (address.endsWith("/health/ready") && overrides.readyStatus) return new Response(null, { status: overrides.readyStatus });
    if (/\/health\/(live|ready)$/.test(address)) return new Response("Healthy");
    throw new Error("Unexpected URL");
  } };
}

test("approved probes are anonymous GETs with bounded bodies and no redirects", async () => {
  const f = fixture();
  const result = await runPreflight(env, options, f.fetcher);
  assert.equal(result.passed, true);
  assert.equal(f.calls.length, 6);
  assert.equal(f.calls[0].address, env.OIDC_ISSUER + ".well-known/openid-configuration");
  for (const { address, init } of f.calls) {
    assert.equal(origins.includes(new URL(address).origin), true);
    assert.equal(init.method, "GET"); assert.equal(init.redirect, "manual");
    assert.equal(init.headers.Authorization, undefined); assert.equal(init.headers.Cookie, undefined);
    assert.equal(init.body, undefined); assert.equal(init.signal instanceof AbortSignal, true);
  }
  assert.equal(result.realLogin, "NOT RUN"); assert.equal(result.realKeyRotation, "NOT RUN");
});

test("metadata cannot cause a request to an unapproved JWKS origin", async () => {
  const f = fixture({ metadata: { ...metadata, jwks_uri: "https://unapproved.example.org/keys" } });
  const result = await runPreflight(env, options, f.fetcher);
  assert.equal(result.passed, false);
  assert.equal(f.calls.some(call => call.address.includes("unapproved")), false);
  assert.equal(result.probes.find(probe => probe.name === "oidc-jwks").category, "origin-not-approved");
});

for (const [name, discovery] of [
  ["redirect", () => new Response(null, { status: 302, headers: { location: "https://unapproved.example.org" } })],
  ["malformed JSON", () => new Response("not-json")],
  ["null metadata", () => Response.json(null)],
  ["oversized body", () => new Response("x".repeat(1_000_001))],
  ["transport failure", () => { throw new Error(env.OIDC_CLIENT_SECRET); }]
]) test(`probe rejects ${name} with bounded private diagnostics`, async () => {
  const result = await runPreflight(env, options, fixture({ discovery }).fetcher);
  assert.equal(result.passed, false);
  assert.equal(JSON.stringify(result).includes(env.OIDC_CLIENT_SECRET), false);
});

test("private or symmetric JWKS data does not pass public-signing-key preflight", async () => {
  const f = fixture({ keys: { keys: [{ kty: "RSA", n: "synthetic", e: "AQAB", d: "synthetic-private" }] } });
  const result = await runPreflight(env, options, f.fetcher);
  assert.equal(result.passed, false);
  assert.equal(JSON.stringify(result).includes("synthetic-private"), false);
});

test("malformed JWKS values fail closed without throwing or reporting values", async () => {
  const f = fixture({ keys: { keys: [42, "invalid", { kty: "RSA", n: "synthetic", e: "AQAB", key_ops: {} }] } });
  const result = await runPreflight(env, options, f.fetcher);
  assert.equal(result.passed, false);
});

test("database readiness failure is not successful empty data", async () => {
  const result = await runPreflight(env, options, fixture({ readyStatus: 503 }).fetcher);
  assert.equal(result.passed, false);
  assert.equal(result.probes.find(probe => probe.name === "api-ready").status, 503);
});

test("deployed DevelopmentLocal mode cannot pass anonymous web readiness", async () => {
  const result = await runPreflight(env, options, fixture({ session: { mode: "development-local", authenticated: true } }).fetcher);
  assert.equal(result.probes.find(probe => probe.name === "web-configured-anonymous").passed, false);
  assert.equal(result.passed, false);
});

test("response-body stall is aborted by the configured deadline without timing assertions", async () => {
  const discovery = () => new Response(new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode("{")); } }));
  const result = await runPreflight({ ...env, WEB_AUTH_TIMEOUT_SECONDS: "1" }, options, fixture({ discovery }).fetcher);
  assert.equal(result.passed, false);
  assert.equal(result.probes.find(probe => probe.name === "oidc-discovery").category, "transport-tls-body-or-json-failure");
});

test("CLI rejects unknown arguments without printing their contents", () => {
  const result = spawnSync(process.execPath, ["--conditions=react-server", "scripts/deployment-preflight.mjs", "--secret=synthetic-sensitive"],
    { cwd: new URL("../", import.meta.url), encoding: "utf8", windowsHide: true });
  assert.equal(result.status, 1);
  assert.equal((result.stdout + result.stderr).includes("synthetic-sensitive"), false);
});
