(() => {
  "use strict";
  if (window.ChurchFinder) return;
  const scriptOrigin = new URL(document.currentScript.src).origin;
  const mounts = new WeakMap();

  function mount(target, options = {}) {
    const container = typeof target === "string" ? document.querySelector(target) : target;
    if (!(container instanceof HTMLElement)) throw new TypeError("Choose a widget container.");
    if (mounts.has(container)) throw new Error("This container already has a church map.");
    const endpoint = new URL(options.endpoint || "/embed/churches", scriptOrigin);
    if (!/^https?:$/.test(endpoint.protocol) || endpoint.username || endpoint.password) {
      throw new TypeError("Use an HTTP(S) endpoint.");
    }
    const initialHeight = options.initialHeight ?? 760;
    if (!Number.isFinite(initialHeight) || initialHeight < 480 || initialHeight > 2400) {
      throw new RangeError("initialHeight must be between 480 and 2400 pixels.");
    }
    const instanceId = crypto.randomUUID();
    endpoint.searchParams.set("parentOrigin", location.origin);
    endpoint.searchParams.set("instanceId", instanceId);
    for (const name of ["city", "denomination", "language", "q"]) {
      if (options[name]) endpoint.searchParams.set(name, String(options[name]));
    }
    const frame = document.createElement("iframe");
    frame.title = options.title || "Find a church";
    frame.style.cssText = `display:block;width:100%;height:${Math.round(initialHeight)}px;border:0;`;
    frame.referrerPolicy = "strict-origin-when-cross-origin";
    frame.setAttribute("sandbox", "allow-scripts allow-same-origin allow-popups allow-popups-to-escape-sandbox");
    let settled = false;
    let destroyed = false;
    let resolveReady;
    let rejectReady;
    const ready = new Promise((resolve, reject) => { resolveReady = resolve; rejectReady = reject; });
    const timeout = setTimeout(() => {
      if (!settled) {
        settled = true;
        rejectReady(new Error("The map did not become ready. Check the service, Maps key, and allowed embed origins."));
      }
    }, 20000);
    function receive(event) {
      if (event.origin !== endpoint.origin || event.source !== frame.contentWindow ||
          event.data?.instanceId !== instanceId || settled) return;
      if (!["churchfinder:ready", "churchfinder:error"].includes(event.data.type)) return;
      settled = true;
      clearTimeout(timeout);
      if (event.data.type === "churchfinder:ready") resolveReady(handle);
      else rejectReady(new Error("The map or church data couldn't load. The widget may still show the church list."));
    }
    const handle = {
      frame, ready,
      destroy() {
        if (destroyed) return;
        destroyed = true;
        clearTimeout(timeout);
        window.removeEventListener("message", receive);
        frame.remove();
        mounts.delete(container);
        if (!settled) { settled = true; rejectReady(new Error("Widget removed before loading completed.")); }
      },
    };
    window.addEventListener("message", receive);
    mounts.set(container, handle);
    frame.src = endpoint.href;
    container.append(frame);
    return handle;
  }

  function getEmbedCode() {
    return `<div id="church-map"></div>\n<script src="${scriptOrigin}/churchfinder.js"><\/script>\n<script>\n  const widget = ChurchFinder.mount("#church-map", { initialHeight: 760 });\n  widget.ready.catch(error => console.error(error.message));\n<\/script>`;
  }
  window.ChurchFinder = Object.freeze({ mount, getEmbedCode });
})();
