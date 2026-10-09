import http from 'node:http';
import https from 'node:https';
import { readFile } from 'node:fs/promises';
import { resolve, extname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { BROWSER_FILES } from './static-files.mjs';

const STATIC_ROOT = fileURLToPath(new URL('./', import.meta.url));
const DEFAULT_PORT = 5173; // 前端开发端口，不改动 IIS、MySQL 或现有监听。
const HTTP_STATUS = { ok: 200, notFound: 404, badGateway: 502 }; // 开发代理只返回必要的 HTTP 状态。
const MODULES = new Set(BROWSER_FILES);
const MIME = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg',
  '.png': 'image/png', '.webp': 'image/webp', '.svg': 'image/svg+xml' };

/** 开发专用同源服务器：静态浏览器模块和 Cookie API 代理，证书通过私有环境配置。 */
export async function startDevServer({ port = DEFAULT_PORT, host = '127.0.0.1',
  target = 'https://localhost:7261', pfxPath, pfxPassword, caPath } = {}) {
  const backend = new URL(target);
  const transport = backend.protocol === 'https:' ? https : http;
  const ca = caPath ? await readFile(caPath) : undefined;
  const tls = pfxPath ? { pfx: await readFile(pfxPath), passphrase: pfxPassword } : null;

  /** 转发请求体和 Cookie，多个 Set-Cookie 不合并，后端证书仍严格验证。 */
  function proxy(request, response) {
    const upstream = transport.request(new URL(request.url, backend), {
      method: request.method, headers: { ...request.headers, host: backend.host }, ca
    }, result => {
      response.writeHead(result.statusCode, { ...result.headers, 'Cache-Control': 'no-store' });
      result.pipe(response);
    });
    upstream.on('error', () => {
      if (!response.headersSent) response.writeHead(HTTP_STATUS.badGateway, { 'Content-Type': 'application/json' });
      response.end(JSON.stringify({ message: '无法连接后端，请检查开发服务与证书配置。' }));
    });
    request.on('aborted', () => upstream.destroy());
    request.pipe(upstream);
  }

  /** 只开放浏览器需要的文件；测试、脚本、配置凭据和越界文件不可访问。 */
  async function serve(request, response) {
    response.setHeader('X-Content-Type-Options', 'nosniff');
    response.setHeader('Referrer-Policy', 'no-referrer');
    if (request.url === '/api' || request.url.startsWith('/api/')) return proxy(request, response);
    let relative;
    try { relative = decodeURIComponent(new URL(request.url, 'http://localhost').pathname).slice(1) || 'index.html'; }
    catch { response.writeHead(HTTP_STATUS.notFound).end(); return; } // 非法 URL 编码不进入文件系统。
    const allowed = MODULES.has(relative) || /^assets\/[a-zA-Z0-9_.-]+\.(jpg|jpeg|png|webp|svg)$/.test(relative);
    if (!allowed) { response.writeHead(HTTP_STATUS.notFound).end(); return; }
    try {
      const contents = await readFile(resolve(STATIC_ROOT, relative));
      response.writeHead(HTTP_STATUS.ok, { 'Content-Type': MIME[extname(relative)], 'Cache-Control': 'no-cache' });
      response.end(contents);
    } catch (error) {
      if (error.code !== 'ENOENT') throw error;
      response.writeHead(HTTP_STATUS.notFound).end();
    }
  }

  const server = tls ? https.createServer(tls, serve) : http.createServer(serve);
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, host, resolve);
  });
  return server;
}

// 直接运行才启动监听，导入时供测试使用；生产部署交给 IIS/Nginx。
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const server = await startDevServer({
    port: Number(process.env.PAWHOME_FRONTEND_PORT || DEFAULT_PORT),
    target: process.env.PAWHOME_API_TARGET || 'https://localhost:7261',
    pfxPath: process.env.PAWHOME_DEV_PFX,
    pfxPassword: process.env.PAWHOME_DEV_PFX_PASSWORD,
    caPath: process.env.PAWHOME_DEV_CA
  });
  const scheme = process.env.PAWHOME_DEV_PFX ? 'https' : 'http';
  console.log(`前端开发地址：${scheme}://localhost:${server.address().port}`);
}
