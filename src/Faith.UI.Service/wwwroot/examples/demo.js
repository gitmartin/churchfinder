(() => {
  "use strict";
  const component = document.body.dataset.component || "a";
  const serviceOrigin = location.port === "5242" ? "https://localhost:7241" : location.origin;
  const container = document.getElementById("faith-widget");
  const shell = document.getElementById("widget-shell");
  const status = document.getElementById("connection-status");
  const dimensions = document.getElementById("dimensions");
  function updateDimensions() {
    const frame = container.querySelector("iframe");
    if (frame) dimensions.textContent = `${Math.round(frame.getBoundingClientRect().width)} × ${Math.round(frame.getBoundingClientRect().height)} px`;
  }
  for (const button of document.querySelectorAll("[data-width]")) {
    button.addEventListener("click", () => {
      shell.dataset.compact = String(button.dataset.width === "compact");
      for (const option of document.querySelectorAll("[data-width]")) option.setAttribute("aria-pressed", String(option === button));
      updateDimensions();
    });
  }
  container.addEventListener("faithui:resize", updateDimensions);
  container.addEventListener("faithui:connection", event => {
    status.textContent = event.detail.connected ? "Service connected · SSE sizing" : "Reconnecting to the service…";
  });
  new ResizeObserver(updateDimensions).observe(container);
  const script = document.createElement("script");
  script.src = `${serviceOrigin}/_content/Faith.UI/faith-ui.js`;
  script.onload = () => {
    try {
      document.getElementById("embed-code").textContent = Faith.UI.getEmbedCode(component);
      const widget = Faith.UI.mount(container, {
        endpoint: `${serviceOrigin}/embed/${component}`, initialHeight: 280, minHeight: 120, maxHeight: 2400
      });
      widget.ready.then(() => { status.textContent = "Control connected · SSE height updates"; }).catch(showError);
      window.addEventListener("pagehide", () => widget.destroy());
    } catch (error) { showError(error); }
  };
  script.onerror = () => showError(new Error("The loader is unavailable. Start the service and trust its local HTTPS certificate."));
  document.head.append(script);
  function showError(error) { status.dataset.error = "true"; status.textContent = error.message; }
})();
