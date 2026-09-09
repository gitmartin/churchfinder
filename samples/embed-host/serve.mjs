import { createServer } from "node:http";
import { readFile } from "node:fs/promises";

// Serve the same plain HTML examples on a different origin without copying them.
const exampleRoot = new URL("../../src/Faith.UI.Service/wwwroot/examples/", import.meta.url);
const server = createServer(async (request, response) => {
  const path = new URL(request.url, "http://localhost:5242").pathname;
  if (path === "/favicon.ico") { response.writeHead(204).end(); return; }
  const file = path === "/" ? "a.html" : path.replace(/^\/examples\//, "");
  if (!["a.html", "b.html", "c.html", "demo.css", "demo.js"].includes(file)
    || !["GET", "HEAD"].includes(request.method)) { response.writeHead(404).end("Not found"); return; }
  const type = file.endsWith(".html") ? "text/html" : file.endsWith(".css") ? "text/css" : "text/javascript";
  try {
    const content = await readFile(new URL(file, exampleRoot));
    response.writeHead(200, { "Content-Type": `${type}; charset=utf-8`, "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" });
    response.end(request.method === "HEAD" ? undefined : content);
  } catch { response.writeHead(500).end("Could not load the demo page."); }
});
server.listen(5242, "127.0.0.1", () => console.log("Independent HTML host: http://localhost:5242"));
for (const signal of ["SIGINT", "SIGTERM"]) process.on(signal, () => server.close());
