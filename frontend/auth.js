import { ApiError } from './api.js';
import { currentDialogOperation, closeDialog, escapeHtml, form, input, FIELD_LIMITS, showDialog, showToast } from './ui.js';

/** 统一账号状态、注册登录及邮件恢复，不保存密码、Cookie 或令牌到本地缓存。 */
export function createAuth(api, onChange) {
  let user = null;
  let version = 0; // 每次身份变化推进版本，阻止旧请求重新显示私人资料。
  let afterLogin = null;

  /** 身份变化立即交给应用清空私人列表和弹窗。 */
  function setUser(next) {
    user = next;
    version++;
    onChange(user);
  }

  /** 登录态恢复允许正常的匿名 401，其他连接错误仍提示。 */
  async function restore(current = () => true) {
    const started = version;
    try {
      const account = await api.request('/auth/me');
      if (started === version && current()) setUser(account);
    } catch (error) {
      if (error.status !== 401) showToast(error.message); // 401 表示访客，而非服务故障。
    }
  }

  /** 私人请求只允许在原账号仍登录时返回，避免退出或换号后的迟到响应。 */
  async function privateRequest(path, options) {
    const started = version;
    const data = await api.request(path, options);
    if (!user || version !== started) throw new ApiError(401, '账号状态已变化，请重新打开页面。');
    return data;
  }

  /** 操作前引导登录；登录成功继续最初的明确操作，不重复提交申请。 */
  function requireLogin(operation) {
    if (user) return operation();
    afterLogin = operation;
    show('login');
  }

  /** 展示账号表单；验证和重置令牌保持在当前表单中。 */
  function show(mode = 'login', values = {}) {
    const username = input('userName', '账号', { maxLength: FIELD_LIMITS.name, autocomplete: 'username' }); // 与后端账号上限一致。
    const email = input('email', '邮箱', { type: 'email', value: values.email || user?.email, autocomplete: 'email', maxLength: FIELD_LIMITS.email }); // Identity 邮箱长度。
    const password = input('password', '密码', { type: 'password', autocomplete: mode === 'register' ? 'new-password' : 'current-password' });
    const token = input('token', '邮件中的验证码', { value: values.token, autocomplete: 'off' });
    const titles = { login: '欢迎回到爪爪有家', register: '创建你的账号', forgot: '找回密码', confirm: '验证邮箱', resend: '重发验证邮件', reset: '设置新密码' };
    const fields = {
      login: username + password,
      register: username + email + password + '<p class="field-hint">密码至少 10 个字符，包含大小写字母、数字和符号。验证邮箱后可找回密码。</p>',
      forgot: email,
      confirm: input('userId', '邮件中的账号标识', { value: values.userId || user?.id, autocomplete: 'off' }) + token,
      resend: email,
      reset: email + token + input('newPassword', '新密码', { type: 'password', autocomplete: 'new-password' })
    };
    const submit = { login: '登录', register: '注册', forgot: '发送恢复邮件', confirm: '验证邮箱', resend: '发送验证邮件', reset: '重置密码' };
    const links = mode === 'login'
      ? '<button class="text-link" data-action="auth" data-mode="register">注册账号</button><button class="text-link" data-action="auth" data-mode="forgot">忘记密码</button>'
      : '<button class="text-link" data-action="auth" data-mode="login">返回登录</button>';
    showDialog(`<h2 id="dialog-title">${titles[mode]}</h2>${form('auth', fields[mode], submit[mode], 'data-mode="' + mode + '"')}
      <div class="dialog-actions auth-links">${links}</div>`, { privateContent: true });
  }

  /** 调用现有身份合同，并明确反馈验证/恢复邮件发送结果。 */
  async function submit(target) {
    const started = version;
    const current = currentDialogOperation();
    const mode = target.dataset.mode;
    const body = Object.fromEntries(new FormData(target));
    const routes = { forgot: 'forgot-password', confirm: 'confirm-email', resend: 'resend-confirmation', reset: 'reset-password' };
    const result = await api.request('/auth/' + (routes[mode] || mode), { method: 'POST', body });
    if (mode !== 'login' && (started !== version || !current())) return;
    if (mode === 'login') {
      // 登录响应可能已更新 Cookie；迟到登录只恢复实际身份，不覆盖新的弹窗操作。
      if (started !== version || !current()) { await restore(); return; }
      setUser(result);
      closeDialog();
      showToast('登录成功。');
      const operation = afterLogin;
      afterLogin = null;
      if (operation) await operation();
    } else if (mode === 'register') {
      show('login');
      showToast('注册成功，请登录。验证邮件已发送。');
    } else if (mode === 'confirm') {
      if (user) await restore(current);
      if (current()) closeDialog();
      showToast('邮箱验证成功。');
    } else if (mode === 'reset') {
      setUser(null);
      afterLogin = null;
      show('login');
      showToast('密码已重置，请使用新密码登录。');
    } else {
      const sent = mode === 'forgot' ? 'reset' : 'confirm';
      show(sent, { email: body.email, userId: user?.id });
      showToast('符合条件时邮件会发送，请查看邮箱并填写邮件中的验证码。');
    }
  }

  /** 即使退出请求失败也清空本地私人视图，但不把失败宣称为服务器退出成功。 */
  async function logout() {
    const started = version;
    try { await api.request('/auth/logout', { method: 'POST', body: {} }); }
    finally {
      // 迟到的退出请求不能清除后来登录的另一个账号。
      if (version === started) { afterLogin = null; setUser(null); }
    }
    showToast('已退出登录。');
  }

  /** 后端私人资源返回 401 时同步清空过期会话。 */
  function expire() {
    afterLogin = null;
    setUser(null);
  }

  /** 渲染同一个账号的发布和领养入口，不显示固定角色。 */
  function profile() {
    return user ? `<div><h3>你好，${escapeHtml(user.userName)}</h3><p>同一个账号，可以为动物寻找家，也可以申请领养。</p>
      <p>邮箱：${escapeHtml(user.email)} · ${user.emailConfirmed ? '已验证' : '未验证'}</p></div>
      <div class="dialog-actions">${!user.emailConfirmed ? '<button class="button" data-action="auth" data-mode="resend">发送验证邮件</button><button class="button" data-action="auth" data-mode="confirm">填写验证码</button>' : ''}
      <button class="button" data-action="logout">退出登录</button></div>` : '';
  }

  return { get user() { return user; }, get version() { return version; },
    /** 在调用方 await 后、同步写入 DOM 前再次检查身份，关闭微任务间隙。 */
    isCurrent(started) { return Boolean(user) && version === started; },
    restore, requireLogin, privateRequest, show, submit, logout, expire, profile };
}
