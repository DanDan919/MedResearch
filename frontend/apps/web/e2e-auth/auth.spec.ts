import { expect, test, type Page } from "@playwright/test";

async function signIn(page: Page, identity = "User A", returnTo = "/research") {
  await page.goto(`/login?returnTo=${encodeURIComponent(returnTo)}`);
  await page.getByRole("button", { name: /sign in|switch account/i }).click();
  await expect(page.getByRole("heading", { name: "Synthetic identity provider" })).toBeVisible();
  await page.getByRole("link", { name: identity, exact: true }).click();
}
async function createRun(page: Page, question: string) {
  await page.goto("/research/new");
  await page.getByLabel("Research question").fill(question);
  await page.getByRole("button", { name: "Start Research" }).click();
  await page.waitForURL(/\/research\/[a-f\d-]{36}$/);
  return new URL(page.url()).pathname.split("/").pop()!;
}

test("production code/PKCE login, secure session, BFF creation, history and logout", async ({ page, context, request }) => {
  const errors: string[] = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("console", message => { if (message.type() === "error" && /hydration|script tag/i.test(message.text())) errors.push(message.text()); });
  await page.goto("/"); await expect(page).toHaveURL(/\/login/);
  await signIn(page);
  await expect(page.getByRole("heading", { name: "Research", exact: true })).toBeVisible();
  const session = await (await page.request.get("/api/auth/session")).json();
  expect(session.authenticated).toBe(true); expect(Object.keys(session).sort()).toEqual(["authenticated", "displayName", "expiresAt", "mode", "sessionId"]);
  const cookie = (await context.cookies()).find(cookie => cookie.name === "__Host-medresearch-session")!;
  expect(cookie.httpOnly).toBe(true); expect(cookie.secure).toBe(true); expect(cookie.sameSite).toBe("Lax");
  expect(await page.evaluate(() => document.cookie)).not.toContain("medresearch-session");
  const question = "User A private synthetic authentication research";
  const id = await createRun(page, question);
  expect((await page.request.get(`/api/backend/api/research/${id}`)).status()).toBe(200);
  await page.goto("/research"); await expect(page.getByText(question)).toBeVisible();
  const storage = await page.evaluate(() => ({ local: { ...localStorage }, session: { ...sessionStorage } }));
  expect(JSON.stringify(storage)).not.toMatch(/access.token|refresh.token|eyJ/i);
  expect(await page.content()).not.toMatch(/synthetic-refresh-not-retained|accessToken|clientSecret/);
  const metrics = await (await request.get("https://127.0.0.1:3443/metrics")).json();
  expect(metrics.pkceVerified).toBeGreaterThan(0); expect(metrics.cookieSeenAtIssuer).toBe(false);
  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page).toHaveURL(/\/login/);
  expect((await page.request.get("/api/backend/api/research")).status()).toBe(401);
  await page.goto("/research"); await expect(page).toHaveURL(/\/login/);
  expect(errors).toEqual([]);
});

test("User B server rejection, forged identity headers and independent browser contexts", async ({ browser }) => {
  const a = await browser.newContext({ baseURL: "https://localhost:3441", ignoreHTTPSErrors: true });
  const b = await browser.newContext({ baseURL: "https://localhost:3441", ignoreHTTPSErrors: true });
  try {
    const pa = await a.newPage(); const pb = await b.newPage();
    await signIn(pa); const id = await createRun(pa, "Cross-user private synthetic research");
    await signIn(pb, "User B");
    for (const suffix of ["", "/progress", "/report", "/quantitative", "/provenance"]) {
      const response = await b.request.get(`/api/backend/api/research/${id}${suffix}`, { headers: { "X-Owner-Id": "UserA", "X-Authenticated-Subject": "UserA", Authorization: "Bearer forged" } });
      expect(response.status()).toBe(404); expect(await response.text()).not.toContain("Cross-user private");
    }
    expect((await (await b.request.get("/api/backend/api/research")).json()).items).toEqual([]);
    const metrics = await (await b.request.get("https://127.0.0.1:3443/metrics")).json(); expect(metrics.identityHeaderSeen).toBe(false);
    expect((await b.request.get("http://127.0.0.1:3442/api/research")).status()).toBe(401);
  } finally { await a.close(); await b.close(); }
});

for (const attack of ["Bad state", "Bad nonce", "Bad signature", "Wrong audience", "Wrong subject", "ID as access", "Expired access", "Provider failure", "Deny access"]) {
  test(`OIDC rejects ${attack} and creates no session`, async ({ page }) => {
    await signIn(page, attack);
    await expect(page).toHaveURL(/\/login\?reason=authentication-failed/);
    await expect(page.getByRole("status")).toHaveText("Sign-in failed. Please try again.");
    expect((await (await page.request.get("/api/auth/session")).json()).authenticated).toBe(false);
    expect((await page.request.get("/api/backend/api/research")).status()).toBe(401);
  });
}

test("account switch and other tab clear private data before exposing the new account", async ({ page, context }) => {
  await signIn(page);
  const question = "Cache isolation User A private question"; const id = await createRun(page, question);
  await page.goto("/research"); await expect(page.getByText(question)).toBeVisible();
  const oldSession = await (await page.request.get("/api/auth/session")).json();
  const second = await context.newPage(); await second.goto("/research"); await expect(second.getByText(question)).toBeVisible();
  await signIn(page, "User B");
  await expect(page.getByText("No research yet")).toBeVisible(); await expect(page.getByText(question)).toHaveCount(0);
  const newSession = await (await page.request.get("/api/auth/session")).json(); expect(newSession.sessionId).not.toBe(oldSession.sessionId);
  await expect(second).toHaveURL(/\/login/);
  await second.reload(); await second.getByRole("link", { name: "Continue to workspace" }).click();
  await expect(second.getByText(question)).toHaveCount(0);
  expect((await second.request.get(`/api/backend/api/research/${id}`)).status()).toBe(404);
});

test("expiry requires reauthentication without stale private state or redirect loop", async ({ page }) => {
  await signIn(page, "Short session");
  await expect(page.getByRole("heading", { name: "Research", exact: true })).toBeVisible();
  await expect(page).toHaveURL(/\/login/, { timeout: 15_000 });
  expect((await page.request.get("/api/backend/api/research")).status()).toBe(401);
  await expect(page.getByRole("button", { name: "Sign in", exact: true })).toBeVisible();
  await signIn(page); await expect(page.getByRole("heading", { name: "Research", exact: true })).toBeVisible();
});

test("real server BFF rejects cross-origin mutations, unsafe return and proxy paths", async ({ page }) => {
  await signIn(page);
  for (const endpoint of ["/api/auth/login", "/api/auth/logout", "/api/backend/api/research"]) {
    expect((await page.request.post(endpoint, { headers: { Origin: "https://evil.example" }, data: { question: "not authorized" } })).status()).toBe(403);
  }
  expect((await page.request.get("/api/backend/https://evil.example")).status()).toBe(404);
  await page.goto("/login?returnTo=https://evil.example");
  await page.getByRole("button", { name: "Switch account" }).click();
  await page.getByRole("link", { name: "User A", exact: true }).click();
  await expect(page).toHaveURL("https://localhost:3441/");
});

test("corrupt cookie and callback replay cannot authenticate", async ({ page, context }) => {
  await context.addCookies([{ name: "__Host-medresearch-session", value: "corrupt", domain: "localhost", path: "/", secure: true, httpOnly: true }]);
  await page.goto("/research"); await expect(page).toHaveURL(/\/login/);
  await page.goto("/api/auth/callback?code=bogus&state=bogus"); await expect(page).toHaveURL(/authentication-failed/);
  expect((await page.request.get("/api/backend/api/research")).status()).toBe(401);
  let successfulCallback = "";
  page.on("request", request => { if (new URL(request.url()).pathname === "/api/auth/callback") successfulCallback = request.url(); });
  await signIn(page); await expect(page.getByRole("heading", { name: "Research", exact: true })).toBeVisible();
  expect(successfulCallback).not.toBe("");
  await page.goto(successfulCallback); await expect(page).toHaveURL(/authentication-failed/);
  expect((await page.request.get("/api/backend/api/research")).status()).toBe(401);
});

test("back navigation after logout cannot restore the old private workspace", async ({ page }) => {
  await signIn(page);
  const question = "Private question before back navigation"; await createRun(page, question);
  await page.goto("/research"); await expect(page.getByText(question)).toBeVisible();
  await page.getByRole("button", { name: "Sign out" }).click(); await expect(page).toHaveURL(/\/login/);
  await page.goBack(); await expect(page).toHaveURL(/\/login/);
  await expect(page.getByText(question)).toHaveCount(0);
  expect((await page.request.get("/api/backend/api/research")).status()).toBe(401);
});
