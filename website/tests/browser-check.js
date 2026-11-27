// Browser checks for the SnapForge website (Playwright + axe-core; not needed to build or deploy the site).
//
//   node website/tests/browser-check.js <site-dir> <output-dir> [--label name] [--browsers chromium,firefox,webkit]
//
// Serves <site-dir> at http://127.0.0.1:<port>/SnapForge/ (the GitHub Pages base path), then for every page and
// viewport (phone 375, tablet 768, desktop 1280) checks: HTTP 200, no console errors or CSP violations, no request
// leaving 127.0.0.1, every image decoded, no horizontal page scroll, axe-core WCAG 2.2 A/AA rules. Also checks the
// skip link and keyboard focus order, the mobile menu (keyboard + Escape), reduced motion, a nested 404 URL, and
// takes full-page screenshots. Writes results.json and exits 1 on any failure.
"use strict";
const http = require("http");
const fs = require("fs");
const path = require("path");
const pw = require("playwright");
const axeSource = fs.readFileSync(require.resolve("axe-core/axe.min.js"), "utf8");

const [siteArg, outDir] = process.argv.slice(2);
const siteDir = siteArg && path.resolve(siteArg);
const label = (process.argv.indexOf("--label") > 0 ? process.argv[process.argv.indexOf("--label") + 1] : "site");
const browserNames = (process.argv.indexOf("--browsers") > 0 ? process.argv[process.argv.indexOf("--browsers") + 1] : "chromium").split(",");
if (!siteArg || !outDir) { console.error("usage: browser-check.js <site-dir> <output-dir>"); process.exit(2); }
fs.mkdirSync(outDir, { recursive: true });

const TYPES = { ".html": "text/html; charset=utf-8", ".css": "text/css", ".js": "text/javascript", ".svg": "image/svg+xml",
  ".png": "image/png", ".jpg": "image/jpeg", ".ico": "image/x-icon", ".xml": "application/xml", ".txt": "text/plain" };
const BASE = "/SnapForge/";
const server = http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split("?")[0]);
  if (!url.startsWith(BASE)) { res.writeHead(404); return res.end("outside base path"); }
  let rel = url.slice(BASE.length) || "index.html";
  const file = path.join(siteDir, rel);
  if (!file.startsWith(path.resolve(siteDir)) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
    res.writeHead(404, { "content-type": TYPES[".html"] });            // like GitHub Pages: serve 404.html
    return res.end(fs.readFileSync(path.join(siteDir, "404.html")));
  }
  res.writeHead(200, { "content-type": TYPES[path.extname(file)] || "application/octet-stream" });
  res.end(fs.readFileSync(file));
});

const VIEWPORTS = [
  { name: "phone", width: 375, height: 812 },
  { name: "tablet", width: 768, height: 1024 },
  { name: "desktop", width: 1280, height: 900 },
];

(async () => {
  await new Promise(r => server.listen(0, "127.0.0.1", r));
  const origin = `http://127.0.0.1:${server.address().port}`;
  const pages = fs.readdirSync(siteDir).filter(f => f.endsWith(".html")).sort();
  const results = { label, origin: origin + BASE, browsers: {}, failures: [] };
  const fail = (msg) => { results.failures.push(msg); console.log("FAIL " + msg); };

  for (const bname of browserNames) {
    let browser;
    try { browser = await pw[bname].launch(); }
    catch (e) { results.browsers[bname] = "not available: " + e.message.split("\n")[0]; console.log(`SKIP ${bname}: not installed`); continue; }
    const br = results.browsers[bname] = { pages: {} };
    for (const vp of VIEWPORTS) {
      const ctx = await browser.newContext({ viewport: { width: vp.width, height: vp.height }, deviceScaleFactor: 1,
        reducedMotion: vp.name === "phone" ? "reduce" : "no-preference" });
      for (const name of pages) {
        const page = await ctx.newPage();
        const errors = [], outside = [];
        page.on("console", m => { if (m.type() === "error") errors.push(m.text()); });
        page.on("pageerror", e => errors.push(e.message));
        page.on("request", r => { if (!r.url().startsWith(origin)) outside.push(r.url()); });
        const resp = await page.goto(origin + BASE + name, { waitUntil: "load" });
        const key = `${bname}/${vp.name}/${name}`;
        const status = resp ? resp.status() : 0;
        if (status !== 200 && name !== "404.html") fail(`${key}: HTTP ${status}`);
        if (errors.length) fail(`${key}: console errors ${JSON.stringify(errors)}`);
        if (outside.length) fail(`${key}: requests outside the site ${JSON.stringify(outside)}`);
        // make lazy images load now (so every image file is checked), then wait for them
        await page.evaluate(() => document.querySelectorAll("img[loading=lazy]").forEach(i => { i.loading = "eager"; }));
        await page.waitForFunction(() => [...document.images].every(i => i.complete), null, { timeout: 10000 }).catch(() => {});
        const info = await page.evaluate(() => ({
          overflow: document.documentElement.scrollWidth - window.innerWidth,
          brokenImages: [...document.images].filter(i => !i.complete || i.naturalWidth === 0).map(i => i.getAttribute("src")),
          cookies: document.cookie,
          title: document.title,
        }));
        if (info.overflow > 0) fail(`${key}: horizontal scroll of ${info.overflow}px`);
        if (info.brokenImages.length) fail(`${key}: images not loaded ${info.brokenImages}`);
        if (info.cookies) fail(`${key}: cookies set`);
        let axe = { violations: [] };
        if (bname === "chromium") {
          await page.evaluate(axeSource);  // via DevTools, so the page's CSP stays enforced
          axe = await page.evaluate(async () => await window.axe.run(document, {
            runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa", "best-practice"] } }));
          for (const v of axe.violations) fail(`${key}: axe ${v.id} (${v.impact}) — ${v.help} — ${v.nodes.slice(0, 3).map(n => n.target.join(" ")).join(" | ")}`);
        }
        br.pages[key] = { status, overflow: info.overflow, axeViolations: axe.violations.map(v => v.id), title: info.title };
        if (bname === "chromium" || name === "index.html")
          await page.screenshot({ path: path.join(outDir, `${label}-${bname}-${vp.name}-${name.replace(".html", "")}.png`), fullPage: true });
        await page.close();
      }

      // keyboard: first Tab reaches the skip link, Enter moves focus to <main>
      const page = await ctx.newPage();
      await page.goto(origin + BASE + "index.html");
      await page.keyboard.press("Tab");
      const first = await page.evaluate(() => document.activeElement && document.activeElement.className);
      if (first !== "skip-link") fail(`${bname}/${vp.name}: first Tab focuses "${first}", expected the skip link`);
      await page.keyboard.press("Enter");
      const afterSkip = await page.evaluate(() => document.activeElement && document.activeElement.id);
      if (afterSkip !== "main") fail(`${bname}/${vp.name}: skip link moved focus to "${afterSkip}"`);
      // focus is always visible: Tab through the page and check each focused element has an outline or box-shadow
      await page.goto(origin + BASE + "download.html");
      const invisible = [];
      for (let i = 0; i < 25; i++) {
        await page.keyboard.press("Tab");
        const f = await page.evaluate(() => {
          const el = document.activeElement; if (!el || el === document.body) return null;
          const cs = getComputedStyle(el); const r = el.getBoundingClientRect();
          return { tag: el.tagName, text: (el.textContent || "").trim().slice(0, 30), outline: cs.outlineStyle !== "none" && parseFloat(cs.outlineWidth) >= 2,
                   visible: r.width > 0 && r.height > 0 };
        });
        if (f && (!f.outline || !f.visible)) invisible.push(f);
      }
      if (invisible.length) fail(`${bname}/${vp.name}: focus not visible on ${JSON.stringify(invisible.slice(0, 3))}`);
      // mobile menu
      if (vp.width <= 960) {
        await page.goto(origin + BASE + "index.html");
        const navVisible = () => page.evaluate(() => getComputedStyle(document.getElementById("site-nav")).display !== "none");
        if (await navVisible()) fail(`${bname}/${vp.name}: navigation should be collapsed behind the Menu button`);
        await page.focus(".nav-toggle");
        await page.keyboard.press("Enter");
        const expanded = await page.getAttribute(".nav-toggle", "aria-expanded");
        if (expanded !== "true" || !(await navVisible())) fail(`${bname}/${vp.name}: Menu button does not open the navigation`);
        await page.screenshot({ path: path.join(outDir, `${label}-${bname}-${vp.name}-menu-open.png`) });
        await page.keyboard.press("Escape");
        if (await navVisible()) fail(`${bname}/${vp.name}: Escape does not close the menu`);
        const back = await page.evaluate(() => document.activeElement.className);
        if (!String(back).includes("nav-toggle")) fail(`${bname}/${vp.name}: focus not returned to the Menu button after Escape`);
      }
      // nested missing URL serves 404.html with working absolute asset paths
      const r404 = await page.goto(origin + BASE + "some/missing/page");
      const ok404 = await page.evaluate(() => [...document.images].every(i => i.naturalWidth > 0) &&
        !!document.querySelector("link[rel=stylesheet]").sheet);
      if (r404.status() !== 404 || !ok404) fail(`${bname}/${vp.name}: nested 404 page broken`);
      await page.close();
      await ctx.close();
    }
    // no-JS: navigation must stay usable
    const ctx = await browser.newContext({ javaScriptEnabled: false, viewport: { width: 375, height: 812 } });
    const page = await ctx.newPage();
    await page.goto(origin + BASE + "index.html");
    const noJsNav = await page.evaluate(() => getComputedStyle(document.getElementById("site-nav")).display !== "none" &&
      getComputedStyle(document.querySelector(".nav-toggle")).display === "none");
    if (!noJsNav) fail(`${bname}/no-js: navigation not visible without JavaScript`);
    await page.screenshot({ path: path.join(outDir, `${label}-${bname}-phone-nojs.png`) });
    await ctx.close();
    await browser.close();
  }
  server.close();
  fs.writeFileSync(path.join(outDir, `${label}-results.json`), JSON.stringify(results, null, 2));
  console.log(results.failures.length ? `${results.failures.length} failure(s)` : "ALL CHECKS PASSED");
  process.exit(results.failures.length ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
