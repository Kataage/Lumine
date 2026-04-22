import { test, expect } from "@playwright/test";

test.describe("Lumine App", () => {
  test("should display the app title", async ({ page }) => {
    await page.goto("/");
    await expect(page).toHaveTitle(/Lumine/);
  });

  test("should have a sidebar", async ({ page }) => {
    await page.goto("/");
    const sidebar = page.locator('[data-testid="sidebar"]');
    await expect(sidebar).toBeVisible();
  });
});