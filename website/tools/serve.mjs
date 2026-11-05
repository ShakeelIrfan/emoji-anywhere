// Tiny local preview server for the website (no dependencies). node website/tools/serve.mjs
import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize } from "node:path";
const root = new URL("..", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1");
const types = { ".html": "text/html; charset=utf-8", ".css": "text/css", ".js": "text/javascript", ".png": "image/png", ".ico": "image/x-icon",
  ".svg": "image/svg+xml", ".woff2": "font/woff2", ".mp4": "video/mp4", ".webm": "video/webm", ".json": "application/json", ".txt": "text/plain", ".xml": "application/xml" };
createServer(async (req, res) => {
  let path = decodeURIComponent(new URL(req.url, "http://x").pathname);
  if (path.endsWith("/")) path += "index.html";
  let file = normalize(join(root, path));
  if (!file.startsWith(normalize(root))) { res.writeHead(403).end(); return; }
  try { if (!extname(file)) file += ".html"; await stat(file); }
  catch { file = join(root, "404.html"); res.statusCode = 404; }
  res.setHeader("Content-Type", types[extname(file)] ?? "application/octet-stream");
  res.end(await readFile(file));
}).listen(4321, "127.0.0.1", () => console.log("http://localhost:4321"));
