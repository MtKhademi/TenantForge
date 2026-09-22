// Dependency-free static SPA server for browser QA (Windows Node).
// Serves the production dist/ with correct MIME types and a SPA fallback to
// index.html for client-side routes. Listens on 127.0.0.1 so the same-OS
// system Chrome (driven by Playwright) can reach it over localhost.
const http = require('http');
const fs = require('fs');
const path = require('path');

const DIST = path.join(__dirname, '..', 'src', 'web', 'dist');
const PORT = 8099;

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.woff2': 'font/woff2',
  '.woff': 'font/woff',
  '.ico': 'image/x-icon',
  '.json': 'application/json; charset=utf-8',
  '.map': 'application/json; charset=utf-8',
};

const server = http.createServer((req, res) => {
  try {
    const url = new URL(req.url, `http://127.0.0.1:${PORT}`);
    let filePath = decodeURIComponent(url.pathname);
    if (filePath === '/') filePath = '/index.html';
    const resolved = path.normalize(path.join(DIST, filePath));
    // Prevent path traversal outside dist.
    if (!resolved.startsWith(DIST)) { res.writeHead(403); res.end('forbidden'); return; }
    fs.stat(resolved, (err, stat) => {
      let target = resolved;
      if (err || !stat.isFile()) {
        // SPA fallback: any non-file GET resolves to index.html.
        target = path.join(DIST, 'index.html');
      }
      fs.readFile(target, (e, data) => {
        if (e) { res.writeHead(404); res.end('not found'); return; }
        const ext = path.extname(target).toLowerCase();
        res.writeHead(200, { 'Content-Type': MIME[ext] || 'application/octet-stream', 'Cache-Control': 'no-store' });
        res.end(data);
      });
    });
  } catch (e) {
    res.writeHead(500); res.end('server error');
  }
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`QA static server: http://127.0.0.1:${PORT} (dist=${DIST})`);
});
