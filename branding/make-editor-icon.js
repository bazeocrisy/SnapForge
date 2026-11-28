// Renders the SnapForge Editor icon (RC3) to PNGs; run: node branding/make-editor-icon.js && python3 branding/pack-ico.py editor
const { chromium } = require("playwright");
const fs = require("fs"), path = require("path");
(async () => {
  const b = await chromium.launch();
  const out = path.join(__dirname, "png-editor");
  fs.mkdirSync(out, { recursive: true });
  for (const size of [16, 20, 24, 32, 40, 48, 64, 128, 256]) {
    const svg = size <= 32 ? "snapforge-editor-icon-small.svg" : "snapforge-editor-icon.svg";
    const p = await b.newPage({ viewport: { width: size, height: size }, deviceScaleFactor: 1 });
    const data = fs.readFileSync(path.join(__dirname, svg), "utf8");
    await p.setContent(`<html><body style="margin:0;background:transparent"><img style="display:block;width:${size}px;height:${size}px" src="data:image/svg+xml;base64,${Buffer.from(data).toString("base64")}"></body></html>`);
    await p.screenshot({ path: path.join(out, `icon-${size}.png`), omitBackground: true });
    await p.close();
  }
  await b.close();
})();
