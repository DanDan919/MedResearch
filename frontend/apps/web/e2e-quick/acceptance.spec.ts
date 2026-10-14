import { expect, test, type Page } from "@playwright/test";

const origin = "https://localhost:3441";
const question = "Does sleep deprivation affect memory in adults?";
async function signIn(page: Page, identity = "User A", returnTo = "/quick-research/new") {
  await page.goto(`/login?returnTo=${encodeURIComponent(returnTo)}`);
  await page.getByRole("button", { name: /sign in|switch account/i }).click();
  await page.getByRole("link", { name: identity, exact: true }).click();
}
async function scenario(page: Page, value: string) {
  expect((await page.request.post("/_fixture/control", { data: { action: "quick-scenario", scenario: value } })).status()).toBe(200);
}
async function submit(page: Page) {
  await page.goto("/quick-research/new");
  await page.getByLabel("Your question").fill(question);
  await page.getByRole("button", { name: "Ask question", exact: true }).click();
  await page.waitForURL(/\/quick-research\/[a-f\d-]{36}$/);
  const id = new URL(page.url()).pathname.split("/").pop()!;
  await expect.poll(async () => (await (await page.request.get(`/api/backend/api/quick-research/${id}`)).json()).status,
    { timeout: 20000 }).toMatch(/Completed|Failed/);
  return { id, details: await (await page.request.get(`/api/backend/api/quick-research/${id}`)).json() };
}
test.beforeEach(async ({ context }) => {
  // Continue all actual requests, but reject any unexpected external browser traffic.
  await context.route("**/*", route => {
    if (!["localhost", "127.0.0.1"].includes(new URL(route.request().url()).hostname)) throw new Error("Unexpected external browser request in Q4.");
    return route.continue();
  });
});

for (const width of [1280, 390]) {
  test(`actual create -> worker -> PostgreSQL -> answer -> citations -> restart/history at ${width}px`, async ({ page }, info) => {
    const started = Date.now(); const errors: string[] = []; page.on("pageerror", error => errors.push(error.message));
    await page.setViewportSize({ width, height: 900 }); await signIn(page); await scenario(page, "normal");
    const before = await (await page.request.get("/api/backend/api/quick-research")).json();
    let posts = 0; page.on("request", request => { if (request.method() === "POST" && new URL(request.url()).pathname === "/api/backend/api/quick-research") posts++; });
    await page.getByLabel("Your question").fill(question);
    const accepted = page.waitForResponse(r => r.request().method() === "POST" && new URL(r.url()).pathname === "/api/backend/api/quick-research");
    await page.locator("main form button[type=submit]").evaluate((button: HTMLButtonElement) => { button.click(); button.click(); });
    const response = await accepted; expect(response.status()).toBe(202); expect(response.headers()["cache-control"]).toContain("private, no-store");
    await page.waitForURL(/\/quick-research\/[a-f\d-]{36}$/);
    const id = new URL(page.url()).pathname.split("/").pop()!;
    await expect(page.getByRole("heading", { name: "Short answer" })).toBeVisible({ timeout: 20000 });
    const details = await (await page.request.get(`/api/backend/api/quick-research/${id}`)).json();
    expect(details.status).toBe("Completed"); expect(details.answerStatus).toBe("Succeeded"); expect(posts).toBe(1);
    expect(details.retrieval.sources).toHaveLength(1); expect(details.retrieval.sources[0].discoveries).toHaveLength(2);
    expect(details.answer.sourceReferences).toEqual([details.retrieval.sources[0].id]);
    expect(details.answer.findings[0].citations[0].excerpt).toBe(details.retrieval.sources[0].abstract);
    await page.getByRole("button", { name: "View source 1 and supporting excerpt" }).click();
    await expect(page.getByRole("dialog").getByText(details.answer.findings[0].citations[0].excerpt, { exact: true })).toBeVisible();
    await expect(page.getByRole("dialog").getByRole("link", { name: "Open publication" })).toHaveAttribute("href", details.retrieval.sources[0].url);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`source-${width}.png`), fullPage: true }); await page.keyboard.press("Escape");
    await page.screenshot({ path: info.outputPath(`answer-${width}.png`), fullPage: true });
    expect((await page.request.post("/_fixture/control", { data: { action: "api-restart" } })).status()).toBe(200);
    expect(await (await page.request.get(`/api/backend/api/quick-research/${id}`)).json()).toEqual(details);
    await page.goto("/quick-research");
    await expect(page.locator(`main a[href="/quick-research/${id}"]`)).toBeVisible();
    const after = await (await page.request.get("/api/backend/api/quick-research")).json(); expect(after.totalCount).toBe(before.totalCount + 1);
    await info.attach("actual-result", { body: JSON.stringify({ runId: id, fake: true, startedAt: new Date(started).toISOString(), finishedAt: new Date().toISOString(),
      durationMs: Date.now() - started, result: details, medicalSupportVerdict: "cannot verify: synthetic sources, not a clinical test", measuredTokens: null, measuredCost: null }, null, 2), contentType: "application/json" });
    expect(errors).toEqual([]);
  });
}

for (const mode of ["partial", "zero", "no-abstract", "failed", "invalid-json", "invalid-citation", "timeout"]) {
  test(`actual Quick ${mode} is persisted honestly without a false answer or redispatch`, async ({ page }, info) => {
    const started = Date.now(); await signIn(page); await scenario(page, mode);
    const { id, details } = await submit(page);
    if (["zero", "no-abstract"].includes(mode)) {
      expect(details.status).toBe("Completed"); expect(details.answerStatus).toBe("InsufficientEvidence"); expect(details.answer.findings).toEqual([]);
      await expect(page.getByRole("heading", { name: "Insufficient evidence" })).toBeVisible({ timeout: 10000 });
    } else if (mode === "partial") {
      expect(details.status).toBe("Completed"); expect(details.retrieval.outcome).toBe("Partial"); expect(details.answerStatus).toBe("Succeeded");
      await expect(page.getByText(/Search coverage is incomplete/)).toBeVisible({ timeout: 10000 });
    } else {
      expect(details.status).toBe("Failed"); expect(details.answer).toBeNull();
      expect(details.answerStatus).toBe(mode === "timeout" ? "OutcomeUnknown" : "Failed");
      await expect(page.getByRole("heading", { name: "Answer unavailable" })).toBeVisible({ timeout: 10000 });
    }
    expect((await page.request.post("/_fixture/control", { data: { action: "api-restart" } })).status()).toBe(200);
    expect(await (await page.request.get(`/api/backend/api/quick-research/${id}`)).json()).toEqual(details);
    await info.attach("actual-result", { body: JSON.stringify({ runId: id, fake: true, scenario: mode, durationMs: Date.now() - started, result: details,
      medicalSupportVerdict: "cannot verify: synthetic fixture", measuredTokens: null, measuredCost: null }, null, 2), contentType: "application/json" });
  });
}

test("actual JWT/PG rejects User B private runs and sources; anonymous Quick navigation redirects", async ({ browser }) => {
  const a = await browser.newContext({ baseURL: origin, ignoreHTTPSErrors: process.platform === "win32" });
  const b = await browser.newContext({ baseURL: origin, ignoreHTTPSErrors: process.platform === "win32" });
  try {
    const pa = await a.newPage(), pb = await b.newPage();
    await pa.goto("/quick-research/new"); await expect(pa).toHaveURL(/\/login\?returnTo=/);
    await signIn(pa); await scenario(pa, "normal"); const { id, details } = await submit(pa);
    await signIn(pb, "User B", `/quick-research/${id}`);
    await expect(pb.getByRole("heading", { name: "Result not available" })).toBeVisible();
    const foreign = await b.request.get(`/api/backend/api/quick-research/${id}`, { headers: { Authorization: "Bearer forged", "X-Owner-Id": "UserA" } });
    expect(foreign.status()).toBe(404); expect(await foreign.text()).not.toContain(details.retrieval.sources[0].id);
    expect((await (await b.request.get("/api/backend/api/quick-research")).json()).items).toEqual([]);
    expect((await a.request.post("/api/backend/api/quick-research", { headers: { Origin: "https://evil.example" }, data: { question } })).status()).toBe(403);
  } finally { await a.close(); await b.close(); }
});

test("actual PostgreSQL outage does not become empty history or an endless spinner", async ({ page }) => {
  await signIn(page); await scenario(page, "normal");
  try {
    expect((await page.request.post("/_fixture/control", { data: { action: "postgres-pause" } })).status()).toBe(200);
    expect((await page.request.get("http://127.0.0.1:3442/health/live")).status()).toBe(200);
    expect((await page.request.get("/api/backend/health/ready")).status()).toBe(503);
    await page.goto("/quick-research");
    await expect(page.getByRole("heading", { name: "Service unavailable" })).toBeVisible({ timeout: 15000 });
    await expect(page.getByText("No Quick results yet.")).toHaveCount(0);
  } finally {
    expect((await page.request.post("/_fixture/control", { data: { action: "postgres-resume" } })).status()).toBe(200);
    await expect.poll(async () => (await page.request.get("/api/backend/health/ready")).status(), { timeout: 20000 }).toBe(200);
  }
});
