import test from 'node:test';
import assert from 'node:assert/strict';

// 先以明确断言报告尚未实现的 API 模块，而非让模块解析错误遮盖需求。
const module = await import('../api.js').catch(error => {
  if (error.code === 'ERR_MODULE_NOT_FOUND') return {};
  throw error;
});

/** 构造网络边界响应；业务测试不绕过前端请求封装。 */
function json(data, status = 200) {
  return new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json' } });
}

/** 取得待测客户端，缺失实现时输出可读的功能失败。 */
function client(options) {
  assert.equal(typeof module.createApiClient, 'function', '应实现统一 API 客户端');
  return module.createApiClient({ apiBase: '/api' }, options);
}

test('每次写入取新 CSRF，保留 Cookie，且不自动重试提交', async () => {
  const calls = [];
  let tokenId = 0; // 为每次防伪请求生成不同测试令牌。
  const api = client({ fetchImpl: async (url, options) => {
    calls.push({ url, options });
    if (url.endsWith('/auth/csrf')) return json({ token: 'token-' + ++tokenId });
    return new Response(null, { status: 204 }); // 写入成功不返回 JSON。
  } });
  await api.request('/auth/login', { method: 'POST', body: { userName: 'test' } });
  await api.request('/auth/logout', { method: 'POST', body: {} });
  assert.deepEqual(calls.map(call => call.url), ['/api/auth/csrf', '/api/auth/login', '/api/auth/csrf', '/api/auth/logout']);
  assert.ok(calls.every(call => call.options.credentials === 'include'));
  const writes = calls.filter(call => call.options.method === 'POST');
  assert.notEqual(writes[0].options.headers['X-CSRF-TOKEN'], writes[1].options.headers['X-CSRF-TOKEN']);
  assert.equal(writes[0].options.headers['Content-Type'], 'application/json');
});

test('multipart 上传由浏览器生成边界且保留原始 FormData', async () => {
  const body = new FormData();
  body.append('file', new Blob(['image'], { type: 'image/png' }), 'test.png');
  let upload;
  const api = client({ fetchImpl: async (url, options) => {
    if (url.endsWith('/auth/csrf')) return json({ token: 'test-token' });
    upload = options;
    return json({ id: 1 }, 201); // 虚构创建 ID 和 HTTP 创建状态。
  } });
  await api.request('/my/animals/1/photos', { method: 'POST', body });
  assert.equal(upload.body, body);
  assert.equal(upload.headers['Content-Type'], undefined);
});

test('401 通知清空会话，登录失败不误清空其他表单', async () => {
  let revoked = 0; // 记录未认证事件，不模拟登录 Cookie。
  const api = client({ onUnauthorized: () => revoked++, fetchImpl: async url =>
    url.endsWith('/auth/csrf') ? json({ token: 't' }) : json({ title: 'Unauthorized' }, 401) });
  await assert.rejects(api.request('/applications/mine'), error => error.status === 401);
  assert.equal(revoked, 1); // 访问私人资源未认证才撤销会话。
  await assert.rejects(api.request('/auth/login', { method: 'POST', body: {} }));
  assert.equal(revoked, 1);
  await assert.rejects(api.request('/auth/logout', { method: 'POST', body: {} }));
  assert.equal(revoked, 2); // 退出的 401 必须清空本地状态。
});

test('校验及超额错误可显示，服务端内部信息不传给页面', async () => {
  const responses = [
    json({ errors: { Phone: ['手机号格式不正确。'] } }, 400),
    json({ message: '最多同时上架 3 个动物。' }, 409),
    json({ detail: 'private connection string' }, 500)
  ]; // HTTP 校验错误、资源冲突和服务器故障。
  const api = client({ fetchImpl: async () => responses.shift() });
  await assert.rejects(api.request('/animals'), /手机号格式不正确/);
  await assert.rejects(api.request('/animals'), /最多同时上架/);
  await assert.rejects(api.request('/animals'), error => !error.message.includes('private'));
});
