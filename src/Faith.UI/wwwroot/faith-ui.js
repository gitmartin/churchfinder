(() => {
  "use strict";
  if (window.Faith?.UI) return;
  const scriptOrigin = new URL(document.currentScript.src).origin;
  const mounts = new WeakMap();

  function mount(target, options = {}) {
    const container = typeof target === "string" ? document.querySelector(target) : target;
    if (!(container instanceof HTMLElement)) throw new TypeError("A widget container is required.");
    if (mounts.has(container)) throw new Error("This container already has a Faith.UI widget.");
    const endpoint = new URL(options.endpoint || "/embed/a", scriptOrigin);
    if (!/^https?:$/.test(endpoint.protocol) || endpoint.username || endpoint.password) throw new TypeError("Use an HTTP(S) endpoint.");
    const component = endpoint.pathname.split("/").pop();
    if (!["a", "b", "c"].includes(component)) throw new TypeError("Choose component a, b, or c.");
    function dimension(value, fallback, min, max) {
      if (value === undefined) return fallback;
      if (!Number.isFinite(value) || value < min || value > max) throw new RangeError(`Size must be between ${min} and ${max} pixels.`);
      return Math.round(value);
    }
    const minHeight = dimension(options.minHeight, 120, 120, 10000);
    const maxHeight = dimension(options.maxHeight, 10000, minHeight, 10000);
    const initialHeight = dimension(options.initialHeight, Math.min(maxHeight, Math.max(minHeight, 280)), minHeight, maxHeight);
    const width = Math.max(1, Math.min(10000, Math.round(container.getBoundingClientRect().width)));
    for (const [key, value] of Object.entries({ parentOrigin: location.origin, width, initialHeight, minHeight, maxHeight })) endpoint.searchParams.set(key, String(value));

    const frame = document.createElement("iframe");
    frame.title = options.title || `Faith.UI component ${component.toUpperCase()}`;
    frame.style.cssText = `display:block;width:100%;height:${initialHeight}px;border:0;`;
    frame.referrerPolicy = "no-referrer";
    frame.setAttribute("sandbox", "allow-scripts allow-same-origin");
    const controller = new AbortController();
    let connection = null;
    let events = null;
    let settled = false;
    let destroyed = false;
    let resolveReady;
    let rejectReady;
    const ready = new Promise((resolve, reject) => { resolveReady = resolve; rejectReady = reject; });
    const timeout = setTimeout(() => fail(new Error("The control did not load. Check the service and allowed host origins.")), 15000);
    function release() {
      events?.close();
      if (connection) {
        fetch(`${endpoint.origin}/api/embed/connections/${connection.connectionId}`, {
          method: "DELETE", headers: { Authorization: `Bearer ${connection.streamToken}` }, credentials: "omit", keepalive: true
        }).catch(() => {});
      }
    }
    function fail(error) {
      if (destroyed) return;
      if (!settled) { settled = true; rejectReady(error); }
      handle.destroy();
    }
    const handle = {
      frame, ready,
      destroy() {
        if (destroyed) return;
        destroyed = true;
        clearTimeout(timeout);
        controller.abort();
        release();
        frame.remove();
        mounts.delete(container);
        if (!settled) { settled = true; rejectReady(new Error("The widget was removed before it finished loading.")); }
      }
    };
    mounts.set(container, handle);
    container.append(frame);
    async function connect() {
      const response = await fetch(`${endpoint.origin}/api/embed/connections`, {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ component }),
        credentials: "omit", signal: controller.signal
      });
      if (!response.ok) throw new Error("The service could not create an embed connection.");
      connection = await response.json();
      if (![connection.connectionId, connection.streamToken, connection.resizeToken].every(value => typeof value === "string" && /^[a-zA-Z0-9_-]+$/.test(value))) {
        throw new Error("The service returned an invalid connection.");
      }
      if (destroyed) { release(); return; }
      endpoint.searchParams.set("connectionId", connection.connectionId);
      endpoint.searchParams.set("resizeToken", connection.resizeToken);
      events = new EventSource(`${endpoint.origin}/api/embed/connections/${connection.connectionId}/events?token=${encodeURIComponent(connection.streamToken)}`);
      events.addEventListener("connected", () => {
        if (!frame.hasAttribute("src")) frame.src = endpoint.href;
        container.dispatchEvent(new CustomEvent("faithui:connection", { detail: { connected: true } }));
      });
      events.addEventListener("resize", event => {
        let message;
        try { message = JSON.parse(event.data); } catch { return; }
        if (!Number.isFinite(message.height) || message.height <= 0 || message.height > 10000000) return;
        const height = Math.min(maxHeight, Math.max(minHeight, Math.ceil(message.height)));
        frame.style.height = `${height}px`;
        container.dispatchEvent(new CustomEvent("faithui:resize", {
          detail: { connectionId: connection.connectionId, height, contentHeight: message.height, capped: message.height > maxHeight }
        }));
        if (!settled) { settled = true; clearTimeout(timeout); resolveReady(handle); }
      });
      events.onerror = () => container.dispatchEvent(new CustomEvent("faithui:connection", { detail: { connected: false } }));
    }
    connect().catch(fail);
    return handle;
  }
  function getEmbedCode(component = "a") {
    if (!["a", "b", "c"].includes(component)) throw new TypeError("Choose component a, b, or c.");
    return [
      `<div id="faith-${component}"></div>`,
      `<script src="${scriptOrigin}/_content/Faith.UI/faith-ui.js"></script>`,
      "<script>",
      `  const widget = Faith.UI.mount("#faith-${component}", {`,
      `    endpoint: "${scriptOrigin}/embed/${component}",`,
      "    initialHeight: 280,", "    minHeight: 120,", "    maxHeight: 2400", "  });",
      "  widget.ready.catch(error => console.error(error.message));", "</script>"
    ].join("\n");
  }
  window.Faith = window.Faith || {};
  window.Faith.UI = Object.freeze({ mount, getEmbedCode });
})();
