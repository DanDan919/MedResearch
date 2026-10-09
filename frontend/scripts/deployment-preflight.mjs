import { pathToFileURL } from "node:url";
import { readAuthConfig } from "../apps/web/lib/auth/config.ts";
import { readBoundedBody } from "../apps/web/lib/auth/http.ts";

function configured(value) { return typeof value === "string" && value.trim().length > 0; }

export function configurationChecks(env) {
  const config = readAuthConfig(env);
  const checks = [
    { name: "production-web", passed: env.NODE_ENV === "production" },
    { name: "web-oidc-configuration", passed: config.mode === "oidc" },
    { name: "production-api", passed: env.ASPNETCORE_ENVIRONMENT === "Production" },
    { name: "api-jwt-mode", passed: !env.Authentication__Mode || env.Authentication__Mode.toLowerCase() === "jwtbearer" },
    { name: "issuer-alignment", passed: config.mode === "oidc" && env.Authentication__Authority === config.issuer },
    { name: "audience-alignment", passed: config.mode === "oidc" && env.Authentication__Audience === config.audience },
    { name: "database-configuration-present", passed: configured(env.ConnectionStrings__MedResearch) },
    { name: "scientific-worker-disabled", passed: env.ResearchProcessing__Enabled?.toLowerCase() === "false" },
    { name: "tls-validation-enabled", passed: env.NODE_TLS_REJECT_UNAUTHORIZED !== "0" }
  ];
  return { config, checks };
}

function secureEndpoint(value) {
  try {
    const url = new URL(value);
    return url.protocol === "https:" && !url.username && !url.password && !url.hash;
  } catch { return false; }
}

export function metadataChecks(metadata, issuer) {
  const includes = (key, value) => Array.isArray(metadata?.[key]) && metadata[key].includes(value);
  return [
    { name: "metadata-exact-issuer", passed: metadata?.issuer === issuer },
    ...["authorization_endpoint", "token_endpoint", "jwks_uri"].map(key => ({
      name: `metadata-${key}`, passed: secureEndpoint(metadata?.[key])
    })),
    { name: "metadata-code-flow", passed: includes("response_types_supported", "code") },
    { name: "metadata-code-grant", passed: metadata?.grant_types_supported === undefined || includes("grant_types_supported", "authorization_code") },
    { name: "metadata-client-secret-post", passed: includes("token_endpoint_auth_methods_supported", "client_secret_post") },
    { name: "metadata-rs256-id-token", passed: includes("id_token_signing_alg_values_supported", "RS256") },
    { name: "metadata-subject-type", passed: includes("subject_types_supported", "public") || includes("subject_types_supported", "pairwise") },
    { name: "metadata-pkce-s256", passed: includes("code_challenge_methods_supported", "S256") }
  ];
}

function allowedOrigins(values) {
  return new Set(values.map(value => {
    const url = new URL(value);
    if (!["https:", "http:"].includes(url.protocol) || url.username || url.password || url.search || url.hash || url.pathname !== "/")
      throw new Error("Invalid approved origin");
    return url.origin;
  }));
}

export async function runPreflight(env, options = {}, fetcher = fetch) {
  const { config, checks } = configurationChecks(env);
  const result = { scope: "preflight-only", configuration: checks, network: "NOT RUN", probes: [],
    realLogin: "NOT RUN", realOwnerIsolation: "NOT RUN", realKeyRotation: "NOT RUN" };
  if (checks.some(check => !check.passed)) return { ...result, passed: false };
  if (!options.networkApproved) return { ...result, passed: true };

  let origins;
  try { origins = allowedOrigins(options.origins ?? []); }
  catch { return { ...result, passed: false, network: "REJECTED", probes: [{ name: "approved-origins", passed: false }] }; }
  const probe = async (name, address, expectedStatus, json = false) => {
    if (!origins.has(new URL(address).origin)) {
      result.probes.push({ name, passed: false, category: "origin-not-approved" });
      return null;
    }
    const signal = AbortSignal.timeout(config.timeoutSeconds * 1000);
    try {
      // Only anonymous GETs: no code exchange, token request, cookies or redirects.
      const response = await fetcher(address, { method: "GET", redirect: "manual", cache: "no-store", signal,
        headers: { Accept: json ? "application/json" : "text/plain" } });
      const status = response.status;
      if (status !== expectedStatus) {
        await response.body?.cancel();
        result.probes.push({ name, passed: false, status, category: "unexpected-http-status" });
        return null;
      }
      const bytes = await readBoundedBody(response, 1_000_000, signal);
      const body = json ? JSON.parse(new TextDecoder().decode(bytes)) : null;
      result.probes.push({ name, passed: true, status });
      return body;
    } catch {
      result.probes.push({ name, passed: false, category: "transport-tls-body-or-json-failure" });
      return null;
    }
  };

  result.network = "READ-ONLY PROBES";
  const metadata = await probe("oidc-discovery", `${config.issuer.replace(/\/$/, "")}/.well-known/openid-configuration`, 200, true);
  const metadataObject = metadata !== null && typeof metadata === "object" && !Array.isArray(metadata);
  result.probes.push({ name: "metadata-object", passed: metadataObject });
  if (metadataObject) {
    const compatible = metadataChecks(metadata, config.issuer);
    result.probes.push(...compatible);
    if (compatible.every(check => check.passed)) {
      const keys = await probe("oidc-jwks", metadata.jwks_uri, 200, true);
      result.probes.push({ name: "public-signing-key-present", passed: Array.isArray(keys?.keys) && keys.keys.some(key =>
        key && typeof key === "object" && !Array.isArray(key) && !["d", "p", "q", "k"].some(field => field in key) &&
        (!key.use || key.use === "sig") && (!key.key_ops || (Array.isArray(key.key_ops) && key.key_ops.includes("verify"))) &&
        ((key.kty === "RSA" && configured(key.n) && configured(key.e)) ||
          (key.kty === "EC" && key.crv === "P-256" && configured(key.x) && configured(key.y)))) });
    }
  }
  const session = await probe("web-anonymous-session", `${config.webOrigin}/api/auth/session`, 200, true);
  result.probes.push({ name: "web-configured-anonymous", passed: session?.mode === "oidc" && session?.authenticated === false });
  await probe("api-live", `${config.apiBaseUrl}/health/live`, 200);
  await probe("api-ready", `${config.apiBaseUrl}/health/ready`, 200);
  await probe("api-anonymous-denied", `${config.apiBaseUrl}/api/research`, 401);
  return { ...result, passed: result.probes.every(check => check.passed) };
}

export async function main(args = process.argv.slice(2), env = process.env) {
  if (args.some(arg => arg !== "--network-approved" && !arg.startsWith("--allow-origin="))) {
    console.error("Unsupported preflight argument; no configuration values are printed.");
    return 1;
  }
  try {
    const result = await runPreflight(env, { networkApproved: args.includes("--network-approved"),
      origins: args.filter(arg => arg.startsWith("--allow-origin=")).map(arg => arg.slice("--allow-origin=".length)) });
    console.log(JSON.stringify(result, null, 2));
    return result.passed ? 0 : 1;
  } catch {
    console.error("Preflight failed; configuration and remote diagnostics are not disclosed.");
    return 1;
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) process.exitCode = await main();
