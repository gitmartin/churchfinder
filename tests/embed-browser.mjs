import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { mkdir, writeFile } from "node:fs/promises";

const { chromium } = createRequire(import.meta.url)("playwright");
const browser = await chromium.launch({ channel: "msedge", headless: true });
const results = [];
const check = (label, condition) => { assert.ok(condition, label); results.push(label); console.log(`PASS ${label}`); };
try {
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1120, height: 900 } });
  const page = await context.newPage();
  let blazorConnected = false;
  page.on("websocket", socket => { if (socket.url().includes("/_blazor")) blazorConnected = true; });
  await page.goto("https://localhost:7241");
  await page.getByRole("link", { name: "View component" }).first().waitFor();
  check("Landing page links to A, B, and C", await page.getByRole("link", { name: "View component" }).count() === 3);
  await mkdir("artifacts", { recursive: true });
  await page.screenshot({ path: "artifacts/catalog.png", fullPage: true });
  await page.getByRole("link", { name: "View component" }).first().click();
  await page.getByRole("link", { name: "Open HTML demo" }).waitFor();
  await page.getByRole("button", { name: "Copy code" }).click();
  await page.getByText("Embed code copied.").or(page.getByText("Select the code above to copy it.")).waitFor();
  check("Component page runs through Blazor Server", blazorConnected);
  await page.getByRole("link", { name: "Open HTML demo" }).click();
  await page.getByText("Control connected · SSE height updates").waitFor();
  let resizeReported = false;
  let sseConnected = false;
  page.on("response", response => {
    if (response.url().includes("/events?token=") && response.status() === 200) sseConnected = true;
    if (response.url().endsWith("/resize") && response.status() === 204) resizeReported = true;
  });
  let releaseControl;
  const controlGate = new Promise(resolve => { releaseControl = resolve; });
  await page.route("https://localhost:7241/embed/a?**", async route => {
    await controlGate;
    await route.continue();
  }, { times: 1 });
  await page.goto("http://127.0.0.1:5242");
  try {
    await page.locator("#faith-a iframe[src]").waitFor({ state: "attached" });
    check("Loading icon is visible while the iframe stays hidden and reserves space",
      await page.locator("#faith-a .faith-ui-loading").isVisible()
      && await page.locator("#faith-a iframe").evaluate(element => getComputedStyle(element).visibility === "hidden"
        && element.getBoundingClientRect().height === 280)
      && await page.locator("#faith-a .faith-ui-embed").getAttribute("aria-busy") === "true");
    await page.screenshot({ path: "artifacts/embed-loading.png", fullPage: true });
  } finally {
    releaseControl();
  }
  await page.getByText("Control connected · SSE height updates").waitFor();
  const frame = page.frameLocator("#faith-a iframe");
  const frameElement = page.locator("#faith-a iframe");
  const height = () => frameElement.evaluate(element => element.getBoundingClientRect().height);
  const initialHeight = await height();
  const iframeUrl = new URL(await frameElement.getAttribute("src"));
  check("Separate HTML host loads the control with sizing requirements", new URL(page.url()).hostname !== iframeUrl.hostname
    && iframeUrl.searchParams.get("width") > 0 && iframeUrl.searchParams.get("initialHeight") === "280"
    && iframeUrl.searchParams.get("maxHeight") === "2400" && iframeUrl.searchParams.get("connectionId")
    && iframeUrl.searchParams.get("resizeToken")
    && await frameElement.isVisible() && await page.locator("#faith-a .faith-ui-loading").isHidden()
    && await page.locator("#faith-a .faith-ui-embed").getAttribute("aria-busy") === "false");

  const responsePromise = page.waitForResponse(response => response.url() === "https://localhost:7241/embed/api/lorem");
  await frame.getByRole("button", { name: "Add Lorem ipsum" }).click();
  const response = await responsePromise;
  await frame.getByText("1 paragraph added. Your text is ready.").waitFor();
  check("Button inserts Lorem ipsum returned by the server", response.ok()
    && (await response.json()).text.startsWith("Lorem ipsum") && await frame.locator("[data-paragraphs] p").count() === 1);
  await page.waitForFunction(initial => parseFloat(document.querySelector("#faith-a iframe").style.height) > initial + 80, initialHeight);
  check("Iframe grows through the service's SSE connection", await height() > initialHeight && resizeReported && sseConnected);
  const wideHeight = await height();
  await page.getByRole("button", { name: "Compact", exact: true }).click();
  await page.waitForFunction(previous => parseFloat(document.querySelector("#faith-a iframe").style.height) > previous + 60, wideHeight);
  check("Narrower width reflows the text and updates height", await height() > wideHeight);
  await frame.getByRole("button", { name: "Clear text" }).click();
  await frame.getByText("Text cleared. Ready when you are.").waitFor();
  await page.getByRole("button", { name: "Wide", exact: true }).click();
  await page.waitForFunction(initial => Math.abs(parseFloat(document.querySelector("#faith-a iframe").style.height) - initial) <= 2, initialHeight);
  check("Clear removes the text and shrinks the iframe", await frame.locator("[data-paragraphs] p").count() === 0
    && Math.abs(await height() - initialHeight) <= 2);

  await frame.getByRole("button", { name: "Add Lorem ipsum" }).click();
  await frame.getByText("1 paragraph added. Your text is ready.").waitFor();
  await page.waitForFunction(initial => parseFloat(document.querySelector("#faith-a iframe").style.height) > initial + 80, initialHeight);
  await mkdir("artifacts", { recursive: true });
  await page.screenshot({ path: "artifacts/embed-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await frame.getByRole("button", { name: "Add Lorem ipsum" }).waitFor();
  const mobileHeight = await frame.locator("#faith-content").evaluate(element => Math.ceil(element.getBoundingClientRect().height));
  await page.waitForFunction(expected => Math.abs(parseFloat(document.querySelector("#faith-a iframe").style.height) - expected) <= 2,
    Math.min(2400, Math.max(120, mobileHeight)));
  await page.screenshot({ path: "artifacts/embed-mobile.png", fullPage: true });
  for (const component of ["b", "c"]) {
    await page.goto(`http://127.0.0.1:5242/component-${component}.html`);
    await page.getByText("Control connected · SSE height updates").waitFor();
    const source = await page.locator(`#faith-${component} iframe`).getAttribute("src");
    assert.equal(new URL(source).pathname, `/embed/${component}`);
  }
  check("B and C load from their inline script examples", true);
  await writeFile("artifacts/embed-browser-results.json", JSON.stringify({ passed: results.length, results }, null, 2));
  console.log(`${results.length} basic browser checks passed.`);
} finally {
  await browser.close();
}
