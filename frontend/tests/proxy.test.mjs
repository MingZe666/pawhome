import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';

const module = await import('../dev-server.mjs').catch(error => {
  if (error.code === 'ERR_MODULE_NOT_FOUND') return {};
  throw error;
});

/** 在操作系统分配的端口启动隔离测试服务器，避免碰到本机已有服务。 */
async function listen(server) {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve)); // 零请求操作系统分配空闲端口。
  return 'http://127.0.0.1:' + server.address().port;
}

/** 测试结束释放监听及连接，不影响其他服务。 */
async function close(server) {
  server.closeAllConnections();
  await new Promise(resolve => server.close(resolve));
}

test('同源代理保留 Cookie、查询参数及多个 Set-Cookie，且不公开工具或越界路径', async () => {
  assert.equal(typeof module.startDevServer, 'function', '应实现前后端同源开发服务器');
  let seen;
  const backend = http.createServer((request, response) => {
    seen = { url: request.url, cookie: request.headers.cookie };
    response.writeHead(200, { 'Content-Type': 'application/json', 'Set-Cookie': ['a=one; HttpOnly', 'b=two; HttpOnly'] });
    response.end(JSON.stringify({ ok: true }));
  });
  const target = await listen(backend);
  const front = await module.startDevServer({ port: 0, host: '127.0.0.1', target });
  const base = 'http://127.0.0.1:' + front.address().port;
  try {
    const response = await fetch(base + '/api/animals?page=2', { headers: { Cookie: 'session=test' } });
    assert.deepEqual(seen, { url: '/api/animals?page=2', cookie: 'session=test' });
    assert.equal(response.headers.getSetCookie().length, 2); // 两个服务端 Cookie 必须独立保留。
    assert.equal((await response.json()).ok, true);
    assert.match(await (await fetch(base + '/')).text(), /爪爪有家/);
    assert.equal((await fetch(base + '/dev-server.mjs')).status, 404);
    assert.equal((await fetch(base + '/tests/api.test.mjs')).status, 404);
    assert.equal((await fetch(base + '/%2e%2e%2fbackend/README.md')).status, 404);
  } finally {
    await close(front);
    await close(backend);
  }
});
