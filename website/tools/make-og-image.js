// Renders tools/og-image.html to public/assets/img/og-image.png (1200x630). Needs Playwright.
const { chromium } = require("playwright");
const path = require("path");
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 });
  await page.goto("file://" + path.join(__dirname, "og-image.html"));
  await page.screenshot({ path: path.join(__dirname, "..", "public", "assets", "img", "og-image.png") });
  await browser.close();
})();
