(() => {
  "use strict";
  const code = document.getElementById("embed-code");
  const component = document.body.dataset.component;
  if (!code || !window.Faith?.UI) return;
  code.textContent = Faith.UI.getEmbedCode(component);
  document.querySelector("[data-copy]").addEventListener("click", async () => {
    const status = document.querySelector("[data-copy-status]");
    try {
      await navigator.clipboard.writeText(code.textContent);
      status.textContent = "Embed code copied.";
    } catch {
      status.textContent = "Select the code above to copy it.";
    }
  });
})();
