(() => {
  "use strict";
  const root = document.getElementById("faith-content");
  if (!root) return;
  const connectionId = root.dataset.connectionId;
  const resizeToken = root.dataset.resizeToken;
  const add = root.querySelector("[data-add]");
  const clear = root.querySelector("[data-clear]");
  const status = root.querySelector("[data-status]");
  const paragraphs = root.querySelector("[data-paragraphs]");
  let lastHeight = 0;
  let scheduled = 0;
  let request = null;
  let generation = 0;
  let pendingHeight = 0;
  let sending = false;
  let retry = 0;
  let stopped = false;

  function report() {
    const height = Math.ceil(root.getBoundingClientRect().height);
    if (height === lastHeight) return;
    lastHeight = height;
    if (!connectionId || !resizeToken) return;
    pendingHeight = height;
    void sendHeight();
  }
  async function sendHeight() {
    if (sending || stopped || !pendingHeight) return;
    sending = true;
    const height = pendingHeight;
    pendingHeight = 0;
    try {
      const response = await fetch(`/api/embed/connections/${encodeURIComponent(connectionId)}/resize`, {
        method: "POST", credentials: "omit", headers: { "Content-Type": "application/json", Authorization: `Bearer ${resizeToken}` },
        body: JSON.stringify({ height }), signal: AbortSignal.timeout(10000)
      });
      if (!response.ok) {
        if ([401, 403, 404, 410].includes(response.status)) {
          stopped = true;
          status.dataset.error = "true";
          status.textContent = "The sizing connection expired. Reload this page to reconnect.";
          return;
        }
        throw new Error("Could not report height.");
      }
    } catch {
      if (!pendingHeight) pendingHeight = height;
      retry = setTimeout(() => { retry = 0; void sendHeight(); }, 1000);
    } finally {
      sending = false;
      if (pendingHeight && !retry && !stopped) void sendHeight();
    }
  }
  function scheduleReport() {
    if (scheduled) return;
    scheduled = requestAnimationFrame(() => {
      scheduled = 0;
      report();
    });
  }
  const observer = new ResizeObserver(scheduleReport);
  observer.observe(root);

  add.addEventListener("click", async () => {
    if (request) return;
    const currentGeneration = generation;
    const controller = new AbortController();
    request = controller;
    const timeout = setTimeout(() => controller.abort(), 10000);
    add.disabled = true;
    status.dataset.error = "false";
    status.textContent = "Getting your paragraph…";
    try {
      const response = await fetch(root.dataset.responseUrl, {
        credentials: "omit",
        cache: "no-store",
        signal: controller.signal
      });
      if (!response.ok) throw new Error("The server could not return a paragraph.");
      const result = await response.json();
      if (typeof result.text !== "string" || !result.text.trim()) throw new Error("The response did not contain text.");
      if (currentGeneration !== generation) return;
      const paragraph = document.createElement("p");
      paragraph.textContent = result.text;
      paragraphs.append(paragraph);
      const count = paragraphs.childElementCount;
      status.textContent = `${count} paragraph${count === 1 ? "" : "s"} added. Your text is ready.`;
      clear.disabled = false;
    } catch (error) {
      if (currentGeneration !== generation) return;
      status.dataset.error = "true";
      status.textContent = "Couldn’t load the text. Please try again.";
    } finally {
      clearTimeout(timeout);
      if (request === controller) request = null;
      add.disabled = false;
    }
  });
  clear.addEventListener("click", () => {
    generation++;
    request?.abort();
    paragraphs.replaceChildren();
    clear.disabled = true;
    status.dataset.error = "false";
    status.textContent = "Text cleared. Ready when you are.";
  });
  window.addEventListener("pagehide", () => {
    stopped = true;
    clearTimeout(retry);
    observer.disconnect();
    cancelAnimationFrame(scheduled);
    request?.abort();
  });
  window.addEventListener("pageshow", () => {
    stopped = false;
    observer.observe(root);
    lastHeight = 0;
    report();
  });
  report();
})();
