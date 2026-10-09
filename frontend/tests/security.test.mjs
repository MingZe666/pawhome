import test from 'node:test';
import assert from 'node:assert/strict';
import { createApiClient } from '../api.js';
import { createAuth } from '../auth.js';
import { createApplications } from '../applications.js';
import { beginDialogOperation, closeDialog, escapeHtml } from '../ui.js';

test('退出后迟到的找回邮件响应不能重新打开私人邮箱表单', async () => {
  let finish;
  const api = { request: path => path === '/auth/me' ? Promise.resolve({ id: 'test', email: 'private@example.test' })
    : new Promise(resolve => { finish = resolve; }) };
  const auth = createAuth(api, () => {});
  await auth.restore();
  const previous = globalThis.FormData;
  // 只替换原生表单读取边界，真实身份模块和版本逻辑保持不变。
  globalThis.FormData = class { *[Symbol.iterator]() { yield ['email', 'private@example.test']; } };
  try {
    const pending = auth.submit({ dataset: { mode: 'forgot' } });
    auth.expire();
    finish(null);
    await pending; // 未绑定 DOM；误渲染会直接导致测试失败。
    assert.equal(auth.user, null);
  } finally { globalThis.FormData = previous; }
});

test('同步关闭立即撤销异步弹窗，新的导航也撤销上一条导航', () => {
  const previous = globalThis.document;
  globalThis.document = { querySelector: selector => selector === '#info-dialog'
    ? { dataset: {}, close() {} } : { replaceChildren() {} } };
  try {
    const old = beginDialogOperation();
    const current = beginDialogOperation();
    assert.equal(old(), false);
    assert.equal(current(), true);
    closeDialog();
    assert.equal(current(), false);
  } finally { globalThis.document = previous; }
});

test('身份检查与调用方恢复之间退出，不得重新渲染联系人', async () => {
  let finish;
  const api = { request: path => path === '/auth/me' ? Promise.resolve({ id: 'test' })
    : new Promise(resolve => { finish = resolve; }) };
  const auth = createAuth(api, () => {});
  await auth.restore();
  const dialog = { dataset: {}, open: false, showModal() { this.open = true; } };
  const body = { innerHTML: '' };
  const previous = globalThis.document;
  globalThis.document = { querySelector: selector => selector === '#info-dialog' ? dialog : body };
  try {
    const pending = createApplications(api, auth).detail(1); // 虚构申请 ID。
    finish({ id: 1, animalId: 1, status: 'Pending', phone: '13800000000' });
    queueMicrotask(() => auth.expire()); // 检查通过后、调用方恢复前的精确窗口。
    await pending;
    assert.equal(dialog.open, false);
    assert.equal(body.innerHTML, '');
  } finally { globalThis.document = previous; }
});

test('退出请求失败或会话过期时仍清空本地私人状态', async () => {
  const api = { request: path => path === '/auth/me' ? Promise.resolve({ id: 'test', emailConfirmed: true })
    : Promise.reject(Object.assign(new Error('会话过期'), { status: 401 })) };
  const states = [];
  const auth = createAuth(api, user => states.push(user));
  await auth.restore();
  await assert.rejects(auth.logout());
  assert.equal(auth.user, null);
  assert.equal(states.at(-1), null);
});

test('退出后迟到的私人响应不能返回给页面', async () => {
  let finish;
  const api = { request: path => path === '/auth/me' ? Promise.resolve({ id: 'test', emailConfirmed: true })
    : new Promise(resolve => { finish = resolve; }) };
  const auth = createAuth(api, () => {});
  await auth.restore();
  const pending = auth.privateRequest('/applications/mine');
  auth.expire();
  finish([{ phone: '13800000000' }]); // 虚构的旧会话联系人。
  await assert.rejects(pending, error => error.status === 401);
});

test('旧会话请求的迟到 401 不应撤销新账号登录态', async () => {
  let version = 1; // 最初登录态版本。
  let revoked = false;
  let finish;
  const api = createApiClient({ apiBase: '/api' }, {
    getSessionVersion: () => version,
    onUnauthorized: () => { revoked = true; },
    fetchImpl: () => new Promise(resolve => { finish = resolve; })
  });
  const pending = api.request('/applications/mine');
  version++; // 请求完成前已切换账号。
  finish(new Response('{}', { status: 401, headers: { 'Content-Type': 'application/json' } }));
  await assert.rejects(pending);
  assert.equal(revoked, false);
});

test('用户字符串统一转义且图片只允许受控 API 路径', () => {
  const payload = '<img src=x onerror="alert(1)">';
  assert.equal(escapeHtml(payload), '&lt;img src=x onerror=&quot;alert(1)&quot;&gt;');
  const api = createApiClient({ apiBase: '/api' });
  assert.equal(api.photoUrl('javascript:alert(1)'), '');
  assert.equal(api.photoUrl('https://outside.test/photo.jpg'), '');
  assert.equal(api.photoUrl('/api/animals/1/photos/2'), '/api/animals/1/photos/2'); // 虚构资源 ID。
});
