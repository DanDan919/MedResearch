import { expect, test } from "@playwright/test";

test("application shell loads and navigates to New Research", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  await page.getByRole("main").getByRole("link", { name: "New Research" }).click();
  await expect(page.getByRole("heading", { name: "New Research" })).toBeVisible();
  await expect(page.getByLabel("Research question")).toBeVisible();
});
