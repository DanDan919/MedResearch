import { expect, test, type Page } from "@playwright/test";
import { quickRunId, shortAnswer, excerpt } from "../../../packages/api/test/fixtures/quick";

async function signIn(page: Page, identity: string, returnTo: string) {
  await page.goto(`/login?returnTo=${encodeURIComponent(returnTo)}`);
  await page.getByRole("button", { name: /sign in|switch account/i }).click();
  await page.getByRole("link", { name: identity, exact: true }).click();
}

test("production Quick BFF verifies owner, no-store and read-only access with independent JWT sessions", async ({ browser }) => {
  const a = await browser.newContext({ baseURL: "https://localhost:3441", ignoreHTTPSErrors: true });
  const b = await browser.newContext({ baseURL: "https://localhost:3441", ignoreHTTPSErrors: true });
  try {
    const pa = await a.newPage(); const pb = await b.newPage();
    const path = `/quick-research/${quickRunId}`;
    await signIn(pa, "User A", path); await expect(pa).toHaveURL("https://localhost:3441" + path);
    await expect(pa.getByRole("heading", { name: "Short answer" })).toBeVisible();
    await pa.getByRole("button", { name: "View source 1 and supporting excerpt" }).click();
    await expect(pa.getByRole("dialog").getByText(excerpt, { exact: true })).toBeVisible();
    const own = await a.request.get(`/api/backend/api/quick-research/${quickRunId}`);
    expect(own.status()).toBe(200); expect(own.headers()["cache-control"]).toContain("private, no-store");
    await signIn(pb, "User B", path);
    await expect(pb.getByRole("heading", { name: "Result not available" })).toBeVisible();
    await expect(pb.getByText(shortAnswer)).toHaveCount(0);
    const foreign = await b.request.get(`/api/backend/api/quick-research/${quickRunId}`, { headers: { "X-Owner-Id": "UserA", Authorization: "Bearer forged" } });
    expect(foreign.status()).toBe(404); expect(await foreign.text()).not.toContain(shortAnswer);
    expect((await b.request.get("/api/backend/api/quick-research/99999999-9999-4999-8999-999999999999")).status()).toBe(404);
    expect((await (await b.request.get("/api/backend/api/quick-research")).json()).items).toEqual([]);
    expect((await a.request.post("/api/backend/api/quick-research", { headers: { Origin: "https://localhost:3441" }, data: { question: "not allowed" } })).status()).toBe(404);
    expect((await a.request.get(`/api/backend/api/quick-research/${quickRunId}`, { headers: { Origin: "https://evil.example" } })).status()).toBe(403);
    expect(await pa.content()).not.toMatch(/accessToken|clientSecret|synthetic-refresh-not-retained/);
  } finally { await a.close(); await b.close(); }
});

test("Quick account switch removes cached answer and open excerpt across tabs", async ({ page, context }) => {
  const path = `/quick-research/${quickRunId}`;
  await signIn(page, "User A", path); await expect(page.getByRole("heading", { name: "Short answer" })).toBeVisible();
  const second = await context.newPage(); await second.goto(path);
  await second.getByRole("button", { name: "View source 1 and supporting excerpt" }).click();
  await expect(second.getByRole("dialog")).toBeVisible();
  await signIn(page, "User B", path);
  await expect(page.getByRole("heading", { name: "Result not available" })).toBeVisible();
  await expect(second).toHaveURL(/\/login/);
  await expect(second.getByText(shortAnswer)).toHaveCount(0); await expect(second.getByText(excerpt)).toHaveCount(0);
  await second.goto(path); await expect(second.getByRole("heading", { name: "Result not available" })).toBeVisible();
  await expect(second.getByRole("dialog")).toHaveCount(0);
});
