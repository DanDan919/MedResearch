import { expect, test, type BrowserContext, type Page } from "@playwright/test";

const runId = process.env.MEDRESEARCH_FIXTURE_RUN_ID!;
const origin = "https://localhost:3441";
const suffixes = ["", "/progress", "/report", "/quantitative", "/provenance"];
async function signIn(page: Page, identity = "User A", returnTo = "/research") {
  await page.goto(`/login?returnTo=${encodeURIComponent(returnTo)}`);
  await page.getByRole("button", { name: /sign in|switch account/i }).click();
  await page.getByRole("link", { name: identity, exact: true }).click();
}
async function guardNetwork(context: BrowserContext) {
  await context.route("**/*", route => {
    const host = new URL(route.request().url()).hostname;
    if (!new Set(["localhost", "127.0.0.1"]).has(host)) throw new Error(`Unexpected external browser request: ${host}`);
    return route.continue();
  });
}
test.beforeEach(async ({ context }) => guardNetwork(context));

test("trusted HTTPS, actual JWT API, persisted report/quantitative/provenance and API restart", async ({ page, context }) => {
  const errors: string[] = []; page.on("pageerror", error => errors.push(error.message));
  await page.goto("/"); await expect(page).toHaveURL(/\/login/);
  expect((await page.request.get("http://127.0.0.1:3442/api/research")).status()).toBe(401);
  await signIn(page);
  const cookie = (await context.cookies()).find(c => c.name === "__Host-medresearch-session")!;
  expect(cookie.httpOnly && cookie.secure).toBe(true); expect(cookie.sameSite).toBe("Lax");
  for (const suffix of suffixes) {
    const response = await page.request.get(`/api/backend/api/research/${runId}${suffix}`);
    expect(response.status(), suffix).toBe(200);
    expect((await response.json()).researchRunId ?? runId).toBe(runId);
  }
  const report = await (await page.request.get(`/api/backend/api/research/${runId}/report`)).json();
  const quantitative = await (await page.request.get(`/api/backend/api/research/${runId}/quantitative`)).json();
  const provenance = await (await page.request.get(`/api/backend/api/research/${runId}/provenance`)).json();
  expect(report.narrativeAuthority).toBe("StructuredClaims"); expect(report.claims.length).toBeGreaterThan(0);
  expect(quantitative[0].snapshotFingerprint).toMatch(/^[a-f\d]{64}$/); expect(provenance.studies).toHaveLength(100);
  expect(provenance.studies.filter((study: { discoveryPaths: unknown[] }) => study.discoveryPaths.length > 1)).toHaveLength(3);
  await page.goto(`/research/${runId}/report`); await expect(page.getByRole("heading", { name: "Research Report", exact: true })).toBeVisible();
  expect((await page.request.post("/_fixture/control", { data: { action: "api-restart" } })).status()).toBe(200);
  expect(await (await page.request.get(`/api/backend/api/research/${runId}/report`)).json()).toEqual(report);
  expect(await (await page.request.get(`/api/backend/api/research/${runId}/quantitative`)).json()).toEqual(quantitative);
  expect(errors).toEqual([]);
});

test("actual database ownership rejects B on every read and ignores forged identity headers", async ({ browser }) => {
  const a = await browser.newContext({ baseURL: origin }); const b = await browser.newContext({ baseURL: origin });
  try {
    await guardNetwork(a); await guardNetwork(b);
    const pa = await a.newPage(), pb = await b.newPage(); await signIn(pa); await signIn(pb, "User B");
    for (const suffix of suffixes) {
      const denied = await b.request.get(`/api/backend/api/research/${runId}${suffix}`, {
        headers: { "X-Owner-Id": "UserA", "X-Authenticated-Subject": "UserA", Authorization: "Bearer forged" } });
      expect(denied.status()).toBe(404); expect(await denied.text()).not.toContain("Does structured sleep improve recall");
      expect((await a.request.get(`/api/backend/api/research/${runId}${suffix}`)).status()).toBe(200);
    }
    expect((await (await b.request.get("/api/backend/api/research")).json()).items).toEqual([]);
  } finally { await a.close(); await b.close(); }
});

test("actual BFF create, CSRF, redirect restriction, switch and logout", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 900 });
  await signIn(page); await page.goto("/research/new");
  const createRequest = page.waitForRequest(request => request.method() === "POST" && new URL(request.url()).pathname === "/api/backend/api/research");
  await page.getByLabel("Research question").fill("User A full stack queued execution");
  await page.getByRole("button", { name: "Start Research" }).click(); await page.waitForURL(/\/research\/[a-f\d-]{36}$/);
  const id = new URL(page.url()).pathname.split("/").pop()!;
  const key = (await createRequest).headers()["idempotency-key"];
  expect(key).toMatch(/^[a-f\d-]{36}$/i);
  expect((await (await page.request.get(`/api/backend/api/research/${id}`)).json()).status).toBe("Queued");
  const retry = await page.request.post("/api/backend/api/research", { headers: { Origin: origin, "Idempotency-Key": key }, data: { question: "User A full stack queued execution" } });
  expect(retry.status()).toBe(201); expect((await retry.json()).researchRunId).toBe(id);
  const conflict = await page.request.post("/api/backend/api/research", { headers: { Origin: origin, "Idempotency-Key": key }, data: { question: "Different full stack research" } });
  expect(conflict.status()).toBe(409); expect((await conflict.json()).code).toBe("admission-idempotency-conflict");
  expect((await page.request.post("/api/backend/api/research", { headers: { Origin: origin }, data: { question: "Missing submission key" } })).status()).toBe(400);
  await page.goto("/research/new");
  await page.getByLabel("Research question").fill("Another queued execution is not allowed");
  await page.getByRole("button", { name: "Start Research" }).click();
  await expect(page.getByText(/Your outstanding research limit has been reached/)).toBeVisible();
  await expect(page).toHaveURL(/\/research\/new$/);
  for (const path of ["/api/auth/login", "/api/auth/logout", "/api/backend/api/research"])
    expect((await page.request.post(path, { headers: { Origin: "https://evil.example" }, data: {} })).status()).toBe(403);
  expect((await page.request.get("/api/backend/https://evil.example")).status()).toBe(404);
  await signIn(page, "User B", "https://evil.example"); await expect(page).toHaveURL(origin + "/");
  expect((await page.request.get(`/api/backend/api/research/${id}`)).status()).toBe(404);
  await page.getByRole("button", { name: "Sign out" }).click(); await expect(page).toHaveURL(/\/login/);
  expect((await page.request.get("/api/backend/api/research")).status()).toBe(401);
});

for (const failure of ["Wrong audience", "Bad signature", "Expired access", "Bad state", "Bad nonce", "Provider failure"]) {
  test(`full-stack login rejects ${failure}`, async ({ page }) => {
    await signIn(page, failure); await expect(page).toHaveURL(/authentication-failed/);
    expect((await page.request.get("/api/backend/api/research")).status()).toBe(401);
  });
}
test("full-stack session expires without restoring private data", async ({ page }) => {
  await signIn(page, "Short session"); await expect(page.getByRole("heading", { name: "Research", exact: true })).toBeVisible();
  await expect(page).toHaveURL(/\/login/, { timeout: 15_000 });
  expect((await page.request.get(`/api/backend/api/research/${runId}/report`)).status()).toBe(401);
});

test("issuer outage and corrupt session never appear as empty scientific results", async ({ page, context }) => {
  await context.addCookies([{ name: "__Host-medresearch-session", value: "corrupt", domain: "localhost", path: "/", httpOnly: true, secure: true }]);
  await page.goto(`/research/${runId}/report`); await expect(page).toHaveURL(/\/login/);
  expect((await page.request.get("/api/backend/api/research")).status()).toBe(401);
  expect((await page.request.get("http://127.0.0.1:3442/api/research", { headers: { Authorization: "Bearer forged" } })).status()).toBe(401);
  try {
    await page.request.post("/_fixture/control", { data: { action: "issuer-stop" } });
    const login = await page.request.post("/api/auth/login", { headers: { Origin: origin, Accept: "application/json" } });
    // Discovery may already be cached; the authorization endpoint must still fail explicitly.
    if (login.status() === 200) expect((await page.request.get((await login.json()).authorizationUrl)).status()).toBe(503);
    else expect(login.status()).toBe(503);
    expect((await (await page.request.get("/api/auth/session")).json()).authenticated).toBe(false);
  } finally { await page.request.post("/_fixture/control", { data: { action: "issuer-start" } }); }
});

test("API and PostgreSQL outages remain errors, liveness survives database outage", async ({ page }) => {
  test.setTimeout(90_000); await signIn(page);
  try {
    expect((await page.request.post("/_fixture/control", { data: { action: "postgres-pause" } })).status()).toBe(200);
    expect((await page.request.get("http://127.0.0.1:3442/health/live")).status()).toBe(200);
    expect((await page.request.get("/api/backend/health/ready")).status()).toBe(503);
    expect((await page.request.get(`/api/backend/api/research/${runId}/report`)).status()).toBe(503);
  } finally {
    expect((await page.request.post("/_fixture/control", { data: { action: "postgres-resume" } })).status()).toBe(200);
    await expect.poll(async () => (await page.request.get("/api/backend/health/ready")).status(), { timeout: 30_000 }).toBe(200);
  }
  try {
    await page.request.post("/_fixture/control", { data: { action: "api-stop" } });
    expect((await page.request.get("/api/backend/api/research")).status()).toBe(503);
    await page.goto("/research"); await expect(page.getByText("No research yet")).toHaveCount(0);
    await expect(page.getByRole("alert")).toBeVisible();
  } finally { await page.request.post("/_fixture/control", { data: { action: "api-restart" } }); }
});

for (const width of [320, 375, 390, 768, 1280]) {
  test(`actual persisted workspaces responsive at ${width}px, 100-study scanning`, async ({ page }, info) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 900 }); await signIn(page);
    const measures = [];
    for (const route of ["/", "/research/new", "/research", `/research/${runId}`, `/research/${runId}/report`, `/research/${runId}/evidence`, `/research/${runId}/quantitative`, "/settings", "/login"]) {
      await page.goto(route);
      if (route.endsWith("/report")) await expect(page.getByRole("heading", { name: "Research Report", exact: true })).toBeVisible();
      else if (route.endsWith("/evidence")) await expect(page.getByRole("heading", { name: "Evidence & Provenance", exact: true })).toBeVisible();
      else if (route.endsWith("/quantitative")) await expect(page.getByRole("heading", { name: "Scientific synthesis workspace", exact: true })).toBeVisible();
      else if (route !== "/login") await expect(page.getByRole("main")).toBeVisible();
      await page.evaluate(() => document.fonts.ready);
      const measure = await page.evaluate(() => ({ clientWidth: document.documentElement.clientWidth, scrollWidth: document.documentElement.scrollWidth,
        offenders: [...document.querySelectorAll("body *")].filter(el => el.getBoundingClientRect().right > document.documentElement.clientWidth + 1)
          .slice(0, 8).map(el => ({ tag: el.tagName, class: el.className })) }));
      measures.push({ route, ...measure });
      expect.soft(measure.scrollWidth, JSON.stringify(measure)).toBeLessThanOrEqual(measure.clientWidth + 1);
      await page.screenshot({ path: info.outputPath(route.replaceAll("/", "_") + ".png"), fullPage: true });
    }
    let provenanceRequests = 0;
    page.on("request", request => { if (new URL(request.url()).pathname.endsWith("/provenance")) provenanceRequests++; });
    await page.goto(`/research/${runId}/evidence`);
    await page.getByLabel("Filter studies").fill("Browser scale study 100");
    await expect(page.locator("article[id^=study-]")).toHaveCount(1);
    await expect(page.getByRole("heading", { name: /Browser scale study 100/ })).toBeVisible();
    expect(provenanceRequests).toBe(1);
    if (width === 390) {
      const filter = page.getByLabel("Filter studies");
      await filter.focus(); await expect(filter).toBeFocused();
      await page.keyboard.press("ControlOrMeta+A"); await page.keyboard.press("Backspace");
      await expect(page.getByText(/Source material metadata \([1-9]/).first()).toBeVisible();
      const settings = page.locator("nav").getByRole("link", { name: "Settings", exact: true });
      await settings.scrollIntoViewIfNeeded(); await settings.focus(); await settings.press("Enter");
      await expect(page).toHaveURL(/\/settings$/);
      await page.goto(`/research/${runId}/report`);
      const support = page.locator("summary").filter({ hasText: /^Claim support$/ }).first();
      await support.focus(); await support.press("Enter");
      await expect(support.locator("..")).toHaveAttribute("open", "");
      await page.goto(`/research/${runId}/quantitative`);
      const plot = page.getByRole("img", { name: "Quantitative contribution plot" });
      await expect(plot).toBeVisible(); await plot.focus(); await expect(plot).toBeFocused();
      await plot.press("ArrowRight");
      await expect.poll(() => plot.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
      expect(await plot.evaluate(element => getComputedStyle(element).outlineStyle)).not.toBe("none");
      const lineage = page.locator("summary").filter({ hasText: /^View IDs$/ }).first();
      await lineage.focus(); await lineage.press("Enter");
      await expect(lineage.locator("..")).toHaveAttribute("open", "");
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(391);
      await page.screenshot({ path: info.outputPath("mobile-keyboard-lineage.png"), fullPage: true });
    }
    await info.attach("responsive-measurements", { body: JSON.stringify(measures, null, 2), contentType: "application/json" });
  });
}
