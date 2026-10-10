import { expect, test } from "@playwright/test";

test("application shell loads and navigates to New Research", async ({ page }) => {
  await page.goto("/");
  const icon = await page.locator('link[rel="icon"]').getAttribute("href");
  expect(icon).toBe("/favicon.ico");
  const iconResponse = await page.request.get(icon!);
  expect(iconResponse.ok()).toBe(true);
  expect(iconResponse.headers()["content-type"]).toContain("image/png");
  expect(Array.from((await iconResponse.body()).subarray(0, 8))).toEqual([137, 80, 78, 71, 13, 10, 26, 10]);
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  await page.getByRole("main").getByRole("link", { name: "New Research" }).click();
  await expect(page.getByRole("heading", { name: "New Research" })).toBeVisible();
  await expect(page.getByLabel("Research question")).toBeVisible();
});

test("application shell has no React runtime or hydration errors", async ({ page }) => {
  const runtimeErrors: string[] = [];
  page.on("pageerror", (error) => runtimeErrors.push(error.message));
  page.on("console", (message) => {
    if (message.type() === "error") {
      runtimeErrors.push(message.text());
    }
  });

  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();

  expect(runtimeErrors.filter((message) => /script tag|hydration failed|hydration mismatch/i.test(message))).toEqual([]);
});

test("research history loads runs and links to details", async ({ page }) => {
  await page.addInitScript(() => {
    const originalFetch = window.fetch.bind(window);
    window.fetch = async (input, init) => {
      const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);

      if (url.includes("/health/ready")) {
        return new Response("Healthy", { status: 200 });
      }

      if (url.includes("/api/research")) {
        return new Response(
          JSON.stringify({
          items: [
            {
              researchRunId: "11111111-1111-4111-8111-111111111111",
              researchQuestionId: "22222222-2222-4222-8222-222222222222",
              question: "Does sleep deprivation impair memory?",
              status: "Completed",
              createdAt: "2026-09-28T12:00:00Z",
              startedAt: "2026-09-28T12:01:00Z",
              completedAt: "2026-09-28T12:05:00Z",
              failureReason: null
            }
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
          totalPages: 1
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        );
      }

      return originalFetch(input, init);
    };
  });

  await page.goto("/research");

  await expect(page.getByRole("heading", { name: "Research" })).toBeVisible();
  await expect(page.getByText("Does sleep deprivation impair memory?")).toBeVisible();
  await expect(page.getByRole("link", { name: /open/i })).toHaveAttribute(
    "href",
    "/research/11111111-1111-4111-8111-111111111111"
  );
});
