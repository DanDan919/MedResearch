import { test, expect } from "@playwright/test";

test("responsive route shells do not overflow at release viewports", async ({ page }, info) => {
  test.setTimeout(120_000);
  await page.addInitScript(() => {
    const original = window.fetch.bind(window);
    window.fetch = async (input, init) => {
      const url = String(input instanceof Request ? input.url : input);
      if (url.includes("/health/ready")) return new Response("Healthy");
      if (url.includes("/api/research")) return new Response(JSON.stringify({ title: "Deterministic unavailable fixture" }), { status: 503 });
      return original(input, init);
    };
  });
  const id = "11111111-1111-4111-8111-111111111111";
  const measures = [];
  for (const width of [320, 375, 390, 768, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    for (const route of ["/", "/research/new", "/research", `/research/${id}`, `/research/${id}/report`, `/research/${id}/evidence`, `/research/${id}/quantitative`, "/login", "/settings"]) {
      await page.goto(route); await page.waitForLoadState("networkidle");
      const measure = await page.evaluate(() => ({
        clientWidth: document.documentElement.clientWidth, scrollWidth: document.documentElement.scrollWidth,
        offenders: [...document.querySelectorAll("body *")].map(element => ({ tag: element.tagName, class: element.className,
          right: element.getBoundingClientRect().right, width: element.getBoundingClientRect().width }))
          .filter(item => item.right > document.documentElement.clientWidth + 1).slice(0, 12)
      }));
      measures.push({ route, width, ...measure });
      if (width === 320) await page.screenshot({ path: info.outputPath(`${route.replaceAll("/", "_") || "dashboard"}.png`), fullPage: true });
      expect.soft(measure.scrollWidth, `${route} at ${width}: ${JSON.stringify(measure.offenders)}`).toBeLessThanOrEqual(measure.clientWidth + 1);
    }
  }
  await info.attach("responsive-measurements", { body: JSON.stringify(measures, null, 2), contentType: "application/json" });
});
