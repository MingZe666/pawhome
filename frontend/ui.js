const TOAST_DURATION_MS = 4200; // 操作提示展示四点二秒。
let toastTimer;
let dialogVersion = 0; // 每次弹窗导航推进版本，阻止旧详情覆盖新表单。

/** 开始异步弹窗导航，返回供响应渲染前同步调用的有效性检查。 */
export function beginDialogOperation() {
  dialogVersion++;
  return currentDialogOperation();
}

/** 捕获当前弹窗供提交或照片刷新使用，不主动改变导航版本。 */
export function currentDialogOperation() {
  const started = dialogVersion;
  return () => started === dialogVersion;
}
export const LIST_PAGE_SIZE = 12; // 动物及申请列表每页十二条，适合手机与三列卡片。
export const FIELD_LIMITS = Object.freeze({
  name: 100, // 人名、动物名及账号字符上限。
  shortText: 32, // 种类与性别等短文本上限。
  address: 300, // 城市及居住地上限。
  description: 2000, // 说明、经验、理由及结果备注上限。
  email: 256, // Identity 邮箱字符上限。
  phone: 20, // 联系电话字段上限。
  weChat: 64 // 微信字段上限。
});

/** 转义所有 API 与表单字符串，统一保护模板中的文本和属性。 */
export function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, character => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  })[character]);
}

/** 复用原生弹窗的焦点与 Esc 处理，私人内容在关闭后也清空。 */
export function showDialog(content, { privateContent = false } = {}) {
  dialogVersion++;
  const dialog = document.querySelector('#info-dialog');
  dialog.dataset.private = String(privateContent);
  document.querySelector('#dialog-body').innerHTML = content;
  if (!dialog.open) dialog.showModal();
  dialog.scrollTop = 0; // 切换内容后从顶部阅读。
}

/** 关闭弹窗并清除表单、令牌或私人申请内容。 */
export function closeDialog() {
  dialogVersion++;
  const dialog = document.querySelector('#info-dialog');
  dialog.close();
  document.querySelector('#dialog-body').replaceChildren();
  delete dialog.dataset.private;
}

/** 用可访问状态反馈操作成功和非表单错误。 */
export function showToast(message) {
  const toast = document.querySelector('#toast');
  clearTimeout(toastTimer);
  toast.textContent = message;
  toast.hidden = false;
  toastTimer = setTimeout(() => { toast.hidden = true; toast.textContent = ''; }, TOAST_DURATION_MS);
}

/** 在表单内部保留错误，用户可以继续修改输入。 */
export function showFormError(form, error) {
  if (!form.isConnected) return showToast(error.message);
  const output = form.querySelector('[data-form-error]');
  output.textContent = error.message;
  output.hidden = false;
}

/** 动态创建有标签的文本输入，HTML5 和后端共同校验。 */
export function input(name, label, { type = 'text', value = '', required = true, maxLength, autocomplete = '' } = {}) {
  return `<label>${label}<input name="${name}" type="${type}" value="${escapeHtml(value)}"
    ${required ? 'required' : ''} ${maxLength ? 'maxlength="' + maxLength + '"' : ''} autocomplete="${autocomplete}"></label>`;
}

/** 多行字段保留换行并统一转义内容。 */
export function textarea(name, label, value = '', maxLength = FIELD_LIMITS.description) {
  // 长文本最多两千字符，与后端描述及理由字段一致。
  return `<label>${label}<textarea name="${name}" required maxlength="${maxLength}">${escapeHtml(value)}</textarea></label>`;
}

/** 所有表单复用相同错误和提交反馈结构。 */
export function form(name, fields, submitLabel, attributes = '') {
  return `<form class="app-form" data-form="${name}" ${attributes}>${fields}
    <p class="form-error" role="alert" data-form-error hidden></p>
    <button class="button primary" type="submit">${submitLabel}</button></form>`;
}

/** 通用加载、空状态及重试区域，不用演示数据掩盖连接失败。 */
export function stateMessage(message, retryAction = '') {
  return `<div class="state-message" role="status"><p>${escapeHtml(message)}</p>
    ${retryAction ? '<button class="button" data-action="' + retryAction + '">重新加载</button>' : ''}</div>`;
}

/** 动物和申请共用页码导航，未知总数时根据本页长度提供下一页。 */
export function pager(current, hasNext, action) {
  return `<div class="pager"><button class="button" data-action="${action}" data-page="${current - 1}" ${current === 1 ? 'disabled' : ''}>上一页</button>
    <span>第 ${current} 页</span><button class="button" data-action="${action}" data-page="${current + 1}" ${hasNext ? '' : 'disabled'}>下一页</button></div>`;
}

/** 禁止重复提交，并在失败后保留填写内容。 */
export async function submitOnce(target, operation) {
  if (target.dataset.busy === 'true') return;
  target.dataset.busy = 'true';
  target.setAttribute('aria-busy', 'true');
  const button = target.querySelector('button[type="submit"]');
  button.disabled = true;
  target.querySelector('[data-form-error]').hidden = true;
  try { await operation(); }
  catch (error) { showFormError(target, error); }
  finally {
    delete target.dataset.busy;
    target.removeAttribute('aria-busy');
    button.disabled = false;
  }
}
