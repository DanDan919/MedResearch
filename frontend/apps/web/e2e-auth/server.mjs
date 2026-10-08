// Synthetic OIDC issuer and API only for the production-build browser suite. Never imported by the app.
import http from "node:http";
import https from "node:https";
import { randomBytes, randomUUID, createHash } from "node:crypto";
import { mkdtemp, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { spawn } from "node:child_process";
import selfsigned from "selfsigned";
import { generateKeyPair, exportJWK, SignJWT, jwtVerify } from "jose";

const issuer = "https://127.0.0.1:3443";
const web = "https://localhost:3441";
const clientId = "synthetic-web-client";
const audience = "synthetic-research-api";
const clientSecret = randomBytes(32).toString("hex");
const sessionSecret = randomBytes(48).toString("hex");
const { privateKey, publicKey } = await generateKeyPair("RS256");
const wrong = await generateKeyPair("RS256");
const jwk = { ...await exportJWK(publicKey), kid: "ephemeral-test-key", use: "sig", alg: "RS256" };
const certificates = await selfsigned.generate([{ name: "commonName", value: "localhost" }], { keySize: 2048, days: 1,
  extensions: [{ name: "basicConstraints", cA: true }, { name: "subjectAltName", altNames: [
    { type: 2, value: "localhost" }, { type: 7, ip: "127.0.0.1" }
  ] }] });
const temporary = await mkdtemp(join(tmpdir(), "medresearch-auth-"));
const ca = join(temporary, "ca.pem"); await writeFile(ca, certificates.cert);
const pending = new Map(); const codes = new Map(); const runs = new Map();
const metrics = { exchanges: 0, pkceVerified: 0, apiRequests: 0, identityHeaderSeen: false, cookieSeenAtIssuer: false };
const servers = [];
const json = (response, status, body) => { response.writeHead(status, { "Content-Type": "application/json", "Cache-Control": "no-store" }); response.end(JSON.stringify(body)); };
const redirect = (response, destination) => { response.writeHead(302, { Location: destination, "Cache-Control": "no-store" }); response.end(); };
async function body(request) {
  let result = ""; for await (const chunk of request) { result += chunk; if (result.length > 16_384) throw new Error("Test body too large"); } return result;
}
function safe(handler) { return async (request, response) => { try { await handler(request, response); } catch { json(response, 500, { title: "Synthetic service failure" }); } }; }
async function sign(subject, aud, nonce, lifetime = 300, signingKey = privateKey) {
  return new SignJWT({ name: subject === "UserA" ? "User A" : "User B", ...(nonce ? { nonce } : {}) })
    .setProtectedHeader({ alg: "RS256", kid: jwk.kid }).setIssuer(issuer).setSubject(subject).setAudience(aud)
    .setIssuedAt().setExpirationTime(Math.floor(Date.now() / 1000) + lifetime).sign(signingKey);
}

const identityServer = https.createServer({ key: certificates.private, cert: certificates.cert }, safe(async (request, response) => {
  const url = new URL(request.url, issuer);
  if (request.headers.cookie?.includes("medresearch")) metrics.cookieSeenAtIssuer = true;
  if (url.pathname === "/.well-known/openid-configuration") return json(response, 200, {
    issuer, authorization_endpoint: issuer + "/authorize", token_endpoint: issuer + "/token", jwks_uri: issuer + "/jwks",
    response_types_supported: ["code"], subject_types_supported: ["public"], id_token_signing_alg_values_supported: ["RS256"],
    token_endpoint_auth_methods_supported: ["client_secret_post"], code_challenge_methods_supported: ["S256"]
  });
  if (url.pathname === "/jwks") return json(response, 200, { keys: [jwk] });
  if (url.pathname === "/metrics") return json(response, 200, metrics);
  if (url.pathname === "/authorize") {
    if (url.searchParams.get("client_id") !== clientId || url.searchParams.get("redirect_uri") !== web + "/api/auth/callback" ||
        url.searchParams.get("code_challenge_method") !== "S256" || !url.searchParams.get("state") || !url.searchParams.get("nonce")) return json(response, 400, { error: "invalid_request" });
    const id = randomUUID(); pending.set(id, Object.fromEntries(url.searchParams));
    response.writeHead(200, { "Content-Type": "text/html", "Cache-Control": "no-store" });
    response.end(`<html><body><h1>Synthetic identity provider</h1>${[
      ["UserA", "normal", "User A"], ["UserB", "normal", "User B"], ["UserA", "bad-state", "Bad state"], ["UserA", "bad-nonce", "Bad nonce"],
      ["UserA", "bad-signature", "Bad signature"], ["UserA", "wrong-audience", "Wrong audience"], ["UserA", "id-as-access", "ID as access"],
      ["UserA", "wrong-subject", "Wrong subject"],
      ["UserA", "expired", "Expired access"], ["UserA", "short", "Short session"], ["UserA", "provider-failure", "Provider failure"],
      ["UserA", "denied", "Deny access"]
    ].map(([user, mode, label]) => `<p><a href="/approve?id=${id}&user=${user}&mode=${mode}">${label}</a></p>`).join("")}</body></html>`);
    return;
  }
  if (url.pathname === "/approve") {
    const params = pending.get(url.searchParams.get("id")); if (!params) return json(response, 400, { error: "invalid_request" });
    pending.delete(url.searchParams.get("id"));
    const mode = url.searchParams.get("mode"); const destination = new URL(params.redirect_uri);
    destination.searchParams.set("state", mode === "bad-state" ? "incorrect-state" : params.state);
    if (mode === "denied") destination.searchParams.set("error", "access_denied");
    else { const code = randomUUID(); codes.set(code, { ...params, user: url.searchParams.get("user"), mode }); destination.searchParams.set("code", code); }
    return redirect(response, destination.href);
  }
  if (url.pathname === "/token" && request.method === "POST") {
    const params = new URLSearchParams(await body(request)); const code = codes.get(params.get("code")); codes.delete(params.get("code")); metrics.exchanges++;
    if (!code || params.get("client_id") !== clientId || params.get("client_secret") !== clientSecret || params.get("redirect_uri") !== code.redirect_uri ||
        createHash("sha256").update(params.get("code_verifier") ?? "").digest("base64url") !== code.code_challenge) return json(response, 400, { error: "invalid_grant" });
    metrics.pkceVerified++;
    if (code.mode === "provider-failure") return json(response, 503, { error: "temporarily_unavailable" });
    const idToken = await sign(code.user, clientId, code.mode === "bad-nonce" ? "incorrect-nonce" : code.nonce, 300, code.mode === "bad-signature" ? wrong.privateKey : privateKey);
    const accessToken = code.mode === "id-as-access" ? idToken : await sign(code.mode === "wrong-subject" ? "UserB" : code.user, code.mode === "wrong-audience" ? "other-api" : audience, null,
      code.mode === "expired" ? -60 : code.mode === "short" ? 8 : 300);
    return json(response, 200, { token_type: "Bearer", access_token: accessToken, id_token: idToken, expires_in: 300,
      refresh_token: "synthetic-refresh-not-retained" });
  }
  json(response, 404, { error: "not_found" });
}));

const apiServer = http.createServer(safe(async (request, response) => {
  const url = new URL(request.url, "http://127.0.0.1:3442");
  if (url.pathname === "/health/ready") { response.end("Healthy"); return; }
  metrics.apiRequests++;
  if (["x-user-id", "x-owner-id", "x-authenticated-subject"].some(key => request.headers[key])) metrics.identityHeaderSeen = true;
  let subject;
  try { subject = (await jwtVerify((request.headers.authorization ?? "").replace(/^Bearer /, ""), publicKey, { issuer, audience })).payload.sub; }
  catch { return json(response, 401, { title: "Unauthorized" }); }
  if (!subject) return json(response, 403, { title: "Invalid subject" });
  if (url.pathname === "/api/research" && request.method === "POST") {
    const incoming = JSON.parse(await body(request)); const id = randomUUID(); const now = new Date().toISOString();
    runs.set(id, { researchRunId: id, researchQuestionId: randomUUID(), question: incoming.question, status: "Queued", createdAt: now,
      startedAt: null, completedAt: null, failureReason: null, owner: subject });
    response.setHeader("Location", "/api/research/" + id); return json(response, 201, { researchRunId: id, status: "Queued" });
  }
  if (url.pathname === "/api/research") {
    const items = [...runs.values()].filter(run => run.owner === subject).map(run => {
      const item = { ...run }; delete item.owner; return item;
    });
    return json(response, 200, { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: items.length ? 1 : 0 });
  }
  const [, id, operation] = url.pathname.match(/^\/api\/research\/([a-f\d-]+)(?:\/(\w+))?$/) ?? [];
  const run = runs.get(id); if (!run || run.owner !== subject) return json(response, 404, { title: "Not found" });
  if (operation === "report") return json(response, 409, { title: "Not ready" });
  if (operation === "quantitative") return json(response, 200, []);
  if (operation === "provenance") return json(response, 200, { researchRunId: id, question: run.question, status: "Queued", createdAt: run.createdAt,
    startedAt: null, completedAt: null, plans: [], searches: [], studies: [], reportClaims: [], quantitativeContributions: [], providerAttempts: [],
    coverage: { researchPlanCount: 0, literatureSearchCount: 0, discoveryPathCount: 0, distinctStudyCount: 0, sourceMaterialCount: 0,
      evidenceExtractionCount: 0, evidenceFindingCount: 0, evidenceEvaluationCount: 0, researchReportClaimCount: 0, hasPersistedProviderFailureProvenance: false } });
  if (operation === "progress") return json(response, 503, { title: "Synthetic progress not implemented" });
  const details = { ...run }; delete details.owner; delete details.researchQuestionId; return json(response, 200, details);
}));

let ready = false;
const webServer = https.createServer({ key: certificates.private, cert: certificates.cert }, (request, response) => {
  if (request.url === "/_fixture/ready") { response.writeHead(ready ? 200 : 503); response.end(); return; }
  const upstream = http.request({ hostname: "127.0.0.1", port: 3440, method: request.method, path: request.url, headers: request.headers }, incoming => {
    response.writeHead(incoming.statusCode, incoming.headers); incoming.pipe(response);
  });
  upstream.on("error", () => { response.writeHead(503); response.end(); }); request.pipe(upstream);
});
for (const [server, port] of [[identityServer, 3443], [apiServer, 3442], [webServer, 3441]]) {
  await new Promise(resolve => server.listen(port, resolve)); servers.push(server);
}
const child = spawn(process.execPath, [resolve("node_modules/next/dist/bin/next"), "start", "--hostname", "127.0.0.1", "--port", "3440"], {
  env: { ...process.env, NODE_ENV: "production", NODE_EXTRA_CA_CERTS: ca, WEB_AUTH_MODE: "Oidc", WEB_AUTH_ORIGIN: web,
    MEDRESEARCH_API_INTERNAL_URL: "http://127.0.0.1:3442", OIDC_ISSUER: issuer, OIDC_CLIENT_ID: clientId, OIDC_CLIENT_SECRET: clientSecret,
    OIDC_API_AUDIENCE: audience, OIDC_SCOPE: "openid profile research", WEB_SESSION_SECRET: sessionSecret }, windowsHide: true, stdio: ["ignore", "pipe", "pipe"]
});
child.stdout.on("data", chunk => { const text = chunk.toString(); if (/Ready in/.test(text)) ready = true; process.stdout.write(text); });
child.stderr.on("data", chunk => process.stderr.write(chunk));
child.on("exit", code => { ready = false; if (!stopping) { console.error("Synthetic Next server exited", code); void stop(1); } });
let stopping = false;
async function stop(code = 0) {
  if (stopping) return; stopping = true; child.kill();
  for (const server of servers) server.closeAllConnections();
  await Promise.all(servers.map(server => new Promise(resolve => server.close(resolve))));
  if (dirname(resolve(temporary)) !== resolve(tmpdir()) || !temporary.includes("medresearch-auth-")) throw new Error("Unsafe temporary cleanup path");
  await rm(temporary, { recursive: true, force: true }); process.exit(code);
}
process.on("SIGINT", () => void stop()); process.on("SIGTERM", () => void stop());
