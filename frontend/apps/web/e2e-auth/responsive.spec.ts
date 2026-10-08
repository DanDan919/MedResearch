import { expect, test } from "@playwright/test";

test("production responsive route shells", async ({ page }, info) => {
  test.setTimeout(120_000);
  await page.goto("/login"); await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("link", { name: "User A", exact: true }).click();
  await page.goto("/research/new"); await page.getByLabel("Research question").fill("Production responsive baseline");
  await page.getByRole("button", { name: "Start Research" }).click(); await page.waitForURL(/\/research\/[a-f\d-]{36}$/);
  const id = new URL(page.url()).pathname.split("/").pop()!;
  const measures = [];
  for (const width of [320, 375, 390, 768, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    for (const route of ["/", "/research/new", "/research", `/research/${id}`, `/research/${id}/report`, `/research/${id}/evidence`, `/research/${id}/quantitative`, "/login", "/settings"]) {
      await page.goto(route);
      if (route !== "/login") await expect(page.getByRole("main")).toBeVisible();
      await page.evaluate(() => document.fonts.ready);
      const measure = await page.evaluate(() => ({ clientWidth: document.documentElement.clientWidth, scrollWidth: document.documentElement.scrollWidth,
        offenders: [...document.querySelectorAll("body *")].filter(el => el.getBoundingClientRect().right > document.documentElement.clientWidth + 1)
          .slice(0, 6).map(el => ({ tag: el.tagName, class: el.className })) }));
      measures.push({ route, width, ...measure });
      if (width === 320 || width === 390) await page.screenshot({ path: info.outputPath(width + route.replaceAll("/", "_") + ".png"), fullPage: true });
      expect.soft(measure.scrollWidth, JSON.stringify({ route, width, ...measure })).toBeLessThanOrEqual(measure.clientWidth + 1);
    }
  }
  await info.attach("production-responsive-measurements", { body: JSON.stringify(measures, null, 2), contentType: "application/json" });
});
