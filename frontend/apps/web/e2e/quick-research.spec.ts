import { expect, test, type Page } from "@playwright/test";
import { quickDetails, quickHistory, quickInsufficient, quickProcessing, quickRunId, shortAnswer, excerpt } from "../../../packages/api/test/fixtures/quick";

async function fixture(page: Page, details = quickDetails(), status = 200) {
  await page.route("**/api/backend/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/health/ready")) return route.fulfill({ status: 200, body: "Healthy" });
    if (path === "/api/backend/api/quick-research") return route.fulfill({ json: quickHistory(details) });
    if (path === `/api/backend/api/quick-research/${quickRunId}`) return route.fulfill({ status, json: status === 200 ? details : { title: "Private upstream error" } });
    return route.fulfill({ status: 503, json: { title: "Unexpected test request" } });
  });
}
async function fits(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }]) {
  test(`Quick completed citations and source dialog fit ${viewport.width}`, async ({ page }, info) => {
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });
    await page.setViewportSize(viewport); await fixture(page);
    await page.goto(`/quick-research/${quickRunId}`);
    await expect(page.getByRole("heading", { name: "Short answer" })).toBeVisible();
    const answer = page.getByText(shortAnswer, { exact: true }).first();
    await expect(answer).toBeVisible();
    expect((await answer.boundingBox())!.y).toBeLessThan(viewport.height);
    await expect(page.getByRole("heading", { name: "Where findings differ" })).toHaveCount(0);
    await expect(page.locator("details")).not.toHaveAttribute("open", ""); await fits(page);
    await page.screenshot({ path: info.outputPath(`quick-result-${viewport.width}.png`), fullPage: true });
    await page.getByRole("button", { name: "View source 1 and supporting excerpt" }).click();
    const dialog = page.getByRole("dialog"); await expect(dialog).toBeVisible();
    await expect(dialog.getByText(excerpt, { exact: true })).toBeVisible();
    await expect(dialog.getByRole("link", { name: "Open publication" })).toHaveAttribute("href", "https://pubmed.ncbi.nlm.nih.gov/12345678/");
    const bounds = (await dialog.boundingBox())!;
    expect(bounds.x).toBeGreaterThanOrEqual(0); expect(bounds.x + bounds.width).toBeLessThanOrEqual(viewport.width);
    await fits(page); await page.screenshot({ path: info.outputPath(`quick-source-${viewport.width}.png`), fullPage: true });
    await page.keyboard.press("Escape"); await expect(dialog).toHaveCount(0);
    await expect(page.getByRole("button", { name: "View source 1 and supporting excerpt" })).toBeFocused();
    expect(errors).toEqual([]);
  });
}

test("Quick history navigation uses saved data and no creation action", async ({ page }) => {
  await fixture(page); await page.goto("/quick-research");
  await page.getByRole("link", { name: /Synthetic question/ }).click();
  await expect(page).toHaveURL(new RegExp(`/quick-research/${quickRunId}$`));
  await expect(page.getByRole("heading", { name: "Short answer" })).toBeVisible();
  await expect(page.getByRole("button", { name: /start|create/i })).toHaveCount(0);
});

test("Quick processing exposes only observed phases, not made-up completion", async ({ page }) => {
  await fixture(page, quickProcessing(true)); await page.goto(`/quick-research/${quickRunId}`);
  const phases = page.getByRole("list", { name: "Quick research progress" });
  await expect(phases.getByRole("listitem")).toHaveCount(3);
  await expect(phases.locator("[aria-current=step]")).toHaveCount(2);
  await expect(page.getByRole("progressbar")).toHaveCount(0);
});

test("Quick insufficient evidence is distinct from transport failure", async ({ page }) => {
  await fixture(page, quickInsufficient()); await page.goto(`/quick-research/${quickRunId}`);
  await expect(page.getByRole("heading", { name: "Insufficient evidence" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Findings" })).toHaveCount(0);
});

test("Quick rejects foreign run response without rendering its private data", async ({ page }) => {
  const d = quickDetails(); d.retrieval!.sources[0].quickRunId = "22222222-2222-4222-8222-222222222222";
  await fixture(page, d); await page.goto(`/quick-research/${quickRunId}`);
  await expect(page.getByRole("heading", { name: "Could not load Quick Research" })).toBeVisible();
  await expect(page.getByText(shortAnswer)).toHaveCount(0);
});

test("Quick long Russian question and unsafe publication URL fit mobile", async ({ page }) => {
  const d = quickDetails(); d.question = "Что известно об исследованиях сна? ".repeat(20);
  d.retrieval!.sources[0].title = "LongPublicationTitle".repeat(35); d.retrieval!.sources[0].url = "javascript:alert(1)";
  await page.setViewportSize({ width: 390, height: 844 }); await fixture(page, d);
  await page.goto(`/quick-research/${quickRunId}`); await expect(page.getByRole("heading", { level: 1 })).toBeVisible(); await fits(page);
  await page.getByRole("button", { name: "View source 1 and supporting excerpt" }).click();
  await expect(page.getByRole("dialog").getByText("Publication link unavailable.")).toBeVisible();
  await expect(page.getByRole("dialog").getByRole("link")).toHaveCount(0); await fits(page);
});
