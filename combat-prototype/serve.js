#!/usr/bin/env node
// Zero-dependency dev server with live reload.
// Usage: node serve.js [port]   (or ./run.sh)
// Serves this folder; injects a tiny script into .html files that reloads
// the page whenever any file in the folder changes.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { exec } from 'node:child_process';

const root = path.dirname(fileURLToPath(import.meta.url));
const port = Number(process.argv[2] || process.env.PORT || 8000);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript',
  '.css': 'text/css', '.json': 'application/json', '.png': 'image/png', '.jpg': 'image/jpeg',
  '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.wav': 'audio/wav', '.mp3': 'audio/mpeg' };

const clients = new Set();
const reloadScript = `<script>
(function(){const es=new EventSource('/__reload');es.onmessage=()=>location.reload();
es.onerror=()=>{es.close();setTimeout(()=>location.reload(),1000);};})();
</script>`;

http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  if (url === '/__reload') {
    res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache', Connection: 'keep-alive' });
    res.write('\n');
    clients.add(res);
    req.on('close', () => clients.delete(res));
    return;
  }
  let file = path.join(root, url === '/' ? 'index.html' : url);
  if (!file.startsWith(root)) { res.writeHead(403); return res.end(); }
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); return res.end('not found: ' + url); }
    const ext = path.extname(file);
    res.writeHead(200, { 'Content-Type': types[ext] || 'application/octet-stream', 'Cache-Control': 'no-store' });
    if (ext === '.html') data = data.toString().replace('</body>', reloadScript + '</body>');
    res.end(data);
  });
}).listen(port, () => {
  const addr = `http://localhost:${port}`;
  console.log(`serving ${root} at ${addr}  (live reload on)`);
  if (!process.argv.includes('--no-open')) exec(`open ${addr}`);
});

// Watch the folder; debounce so an editor's multiple writes cause one reload.
let timer;
fs.watch(root, { recursive: true }, (_, name) => {
  if (!name || name.startsWith('.') || name.includes('node_modules')) return;
  clearTimeout(timer);
  timer = setTimeout(() => {
    console.log(`changed: ${name} -> reload (${clients.size} tab${clients.size === 1 ? '' : 's'})`);
    for (const c of clients) c.write('data: reload\n\n');
  }, 80);
});
