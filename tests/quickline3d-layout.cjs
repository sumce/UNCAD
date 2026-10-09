// Run with node; Playwright must be installed or available through NODE_PATH.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

const webRoot = path.resolve(__dirname, '../src/UNCAD/Web/QuickLine3D');
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css' };
const server = http.createServer(async (request, response) => {
  const relative = decodeURIComponent(new URL(request.url, 'http://localhost').pathname).replace(/^\/+/, '');
  const file = path.resolve(webRoot, relative || 'index.html');
  if (!file.startsWith(webRoot + path.sep)) { response.writeHead(403).end(); return; }
  try {
    const bytes = await fs.readFile(file);
    response.writeHead(200, { 'Content-Type': mime[path.extname(file)] || 'application/octet-stream' }).end(bytes);
  } catch { response.writeHead(404).end(); }
});

function payload(dense) {
  const name = dense ? 'MDIDA03_RTP_Remote_AC_BOX_1_'.repeat(20) : 'MDIDA03';
  const count = dense ? 60 : 1;
  const length = dense ? 123456789.12 : 4800;
  return {
    type: 'initialize', mode: 'batch',
    metadata: { machineId: name, deviceName: name, frameCount: count },
    scene: {
      projectionMode: 'Isometric',
      nodes: [{ id: 'a', position: { x: 0, y: 0, z: 0 } }, { id: 'b', position: { x: 4800, y: 0, z: 0 } }],
      segments: Array.from({ length: count }, (_, i) => ({
        id: String(i), routeGroupId: `route-${i}`, frameLabel: name, circuitName: name,
        startNodeId: 'a', endNodeId: 'b', distanceMm: length, axis: 'X'
      })),
      diagnostics: Array.from({ length: count }, (_, i) => `${i}: ${name}`)
    },
    axisGrid: { spacingMillimetres: 4800,
      numericMarks: Array.from({ length: count }, (_, i) => ({ label: String(i + 1), offsetMillimetres: i * 4800 })),
      alphabeticMarks: [{ label: 'E', offsetMillimetres: 0 }] },
    bridges: Array.from({ length: count }, () => ({ spec: name, lengthM: length / 1000, associated: false })),
    frames: Array.from({ length: count }, (_, i) => ({
      id: String(i), machineId: name, deviceName: name, downstreamAxis: name, upstreamAxis: name,
      offsetXMillimetres: length, offsetYMillimetres: length
    }))
  };
}

async function measure(page, dense) {
  const data = payload(dense);
  await page.evaluate(message => window.dispatchEvent(new MessageEvent('preview-test', { data: message })), data);
  await page.locator('#loading').waitFor({ state: 'hidden' });
  assert.equal(await page.locator('#routeList .route-row').count(), data.scene.segments.length);
  return page.evaluate(() => {
    const selectors = ['.inspector', '#viewport', '#routeList', '#bridgeList', '#frameList', '#axisMarks', '#diagnostics', '.route-row', '.frame-row', '.route-summary'];
    return Object.fromEntries(selectors.map(selector => {
      const element = document.querySelector(selector);
      const box = element.getBoundingClientRect();
      return [selector, { width: box.width, height: box.height, scrollWidth: element.scrollWidth,
        clientWidth: element.clientWidth, scrollHeight: element.scrollHeight, clientHeight: element.clientHeight }];
    }));
  });
}

async function main() {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  let browser;
  try {
    browser = await chromium.launch({ headless: true });
    for (const [width, height, scale] of [[1180, 760, 1], [900, 600, 1.5], [600, 400, 2], [390, 720, 1]]) {
      const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: scale });
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      await page.addInitScript(() => {
        window.chrome = window.chrome || {};
        window.chrome.webview = {
          addEventListener: (_, callback) => window.addEventListener('preview-test', callback),
          postMessage: () => {}
        };
      });
      await page.goto(`http://127.0.0.1:${server.address().port}/`);
      await page.locator('#canvasHost canvas').waitFor();
      const baseline = await measure(page, false);
      const dense = await measure(page, true);
      for (const selector of Object.keys(baseline)) {
        assert.equal(dense[selector].width, baseline[selector].width, `${width}: ${selector} width changed`);
        assert.equal(dense[selector].height, baseline[selector].height, `${width}: ${selector} height changed`);
        assert.ok(dense[selector].scrollWidth <= dense[selector].clientWidth + 1, `${width}: ${selector} horizontal overflow`);
      }
      assert.equal(dense['.inspector'].scrollHeight, baseline['.inspector'].scrollHeight, `${width}: sidebar contents grew`);
      for (const selector of ['#routeList', '#bridgeList', '#frameList', '#diagnostics']) {
        assert.ok(dense[selector].scrollHeight > dense[selector].clientHeight, `${selector} should scroll internally`);
      }
      assert.equal(await page.locator('.route-identity').first().getAttribute('title'),
        await page.locator('.route-identity').first().textContent());
      const canvas = page.locator('#canvasHost canvas');
      const rendered = await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => {
        const sample = document.createElement('canvas');
        sample.width = 64; sample.height = 64;
        const context = sample.getContext('2d');
        context.drawImage(document.querySelector('#canvasHost canvas'), 0, 0, 64, 64);
        const pixels = context.getImageData(0, 0, 64, 64).data;
        const colors = new Set();
        for (let i = 0; i < pixels.length; i += 4) colors.add(`${pixels[i]},${pixels[i + 1]},${pixels[i + 2]}`);
        resolve(colors.size > 3);
      })));
      assert.ok(rendered, `${width}: 3D canvas is blank`);
      const beforeOrbit = await canvas.screenshot();
      const box = await canvas.boundingBox();
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
      await page.mouse.down();
      await page.mouse.move(box.x + box.width * 0.65, box.y + box.height * 0.6, { steps: 8 });
      await page.mouse.up();
      assert.notDeepEqual(await canvas.screenshot(), beforeOrbit, `${width}: orbit did not change the scene`);
      assert.deepEqual(errors, []);
      if (process.env.UNCAD_LAYOUT_SCREENSHOT) {
        const screenshot = process.env.UNCAD_LAYOUT_SCREENSHOT.replace(/\.png$/, `-${width}.png`);
        await fs.mkdir(path.dirname(screenshot), { recursive: true });
        await page.screenshot({ path: screenshot });
      }
      console.log(`PASS ${width}x${height} @${scale}: sidebar and viewport remain bounded`);
      await context.close();
    }
  } finally {
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
