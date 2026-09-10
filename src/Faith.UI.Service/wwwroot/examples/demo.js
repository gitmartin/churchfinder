(() => {
  "use strict";
  function showError(error) {
    const status = document.getElementById("connection-status");
    status.dataset.error = "true";
    status.textContent = error.message || "The control could not be loaded.";
  }

  function attach(widget) {
    const component = document.body.dataset.component;
    const container = document.getElementById(`faith-${component}`);
    const shell = document.getElementById("widget-shell");
    const status = document.getElementById("connection-status");
    const dimensions = document.getElementById("dimensions");
    function updateDimensions() {
      dimensions.textContent = `${Math.round(widget.frame.getBoundingClientRect().width)} × ${Math.round(widget.frame.getBoundingClientRect().height)} px`;
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
    const observer = new ResizeObserver(updateDimensions);
    observer.observe(container);
    document.getElementById("embed-code").textContent = Faith.UI.getEmbedCode(component);
    widget.ready.then(() => {
      status.dataset.error = "false";
      status.textContent = "Control connected · SSE height updates";
      updateDimensions();
    }).catch(showError);
    window.addEventListener("pagehide", () => {
      observer.disconnect();
      widget.destroy();
    }, { once: true });
    updateDimensions();
  }

  window.FaithDemo = Object.freeze({ attach, showError });
})();
