import { beginDialogOperation, currentDialogOperation, closeDialog, escapeHtml as e, form, input, FIELD_LIMITS, LIST_PAGE_SIZE, pager, showDialog, showToast, stateMessage, textarea } from './ui.js';

const STATUS_LABELS = Object.freeze({ Pending: '待沟通', Approved: '已通过', Rejected: '未通过' });

/** 领养申请及发布者收件箱，所有读取经过当前会话检查。 */
export function createApplications(api, auth) {
  let page = 1; // 本人申请页码从一开始。
  let inboxPage = 1;
  let inboxAnimal = null;
  let listSequence = 0; // 同一账号快速翻页时只接受最新响应。

  /** 引导申请人理解联系方式可见范围，并仅收集已确认的字段。 */
  async function showApply(id) {
    const started = auth.version;
    const current = beginDialogOperation();
    const animal = await api.request('/animals/' + id);
    if (!auth.isCurrent(started) || !current()) return;
    showDialog(`<h2 id="dialog-title">申请领养 · ${e(animal.name)}</h2><p class="dialog-note">以下信息仅你本人和该动物发布者可见，发布者可通过电话或微信与你私下联系。</p>
      ${form('application', input('name', '姓名', { maxLength: FIELD_LIMITS.name, autocomplete: 'name' }) +
        input('phone', '手机号（必填）', { type: 'tel', maxLength: FIELD_LIMITS.phone, autocomplete: 'tel' }) +
        input('weChat', '微信号（选填）', { required: false, maxLength: FIELD_LIMITS.weChat }) +
        input('residence', '居住地', { maxLength: FIELD_LIMITS.address, autocomplete: 'street-address' }) +
        textarea('petExperience', '养宠经验') + textarea('reason', '申请理由'), '提交领养申请', 'data-id="' + animal.id + '"')}`, { privateContent: true });
  }

  /** 服务端确认创建后才显示成功，不模拟申请提交。 */
  async function submit(target) {
    const started = auth.version;
    const current = currentDialogOperation();
    const body = Object.fromEntries(new FormData(target));
    body.animalId = Number(target.dataset.id);
    body.weChat = body.weChat.trim() || null;
    await auth.privateRequest('/applications', { method: 'POST', body });
    if (!auth.isCurrent(started)) return;
    if (current()) closeDialog();
    await loadMine();
    showToast('申请已提交，发布者可查看你的联系方式。');
  }

  /** 使用安全文本渲染列表，不在列表中暴露电话或微信。 */
  function listing(items, incoming = false) {
    return items.length ? `<div class="application-list">${items.map(item => `<article class="application-card">
      <div><h4>${incoming ? e(item.name) : '领养动物 #' + item.animalId}</h4><p>${e(STATUS_LABELS[item.status])} · ${e(new Date(item.createdAt).toLocaleString('zh-CN'))}</p></div>
      <button class="button" data-action="application-detail" data-id="${item.id}" ${incoming ? 'data-incoming="true"' : ''}>查看申请</button></article>`).join('')}</div>` : stateMessage(incoming ? '这只动物暂时没有收到申请。' : '还没有领养申请，去遇见一位伙伴吧。');
  }

  /** 本人申请读取完成后检查会话版本，退出后不重新插入旧资料。 */
  async function loadMine() {
    if (!auth.user) return;
    const started = auth.version;
    const sequence = ++listSequence;
    const panel = document.querySelector('#my-applications');
    panel.innerHTML = stateMessage('正在加载你的申请…');
    try {
      const items = await auth.privateRequest(`/applications/mine?page=${page}&pageSize=${LIST_PAGE_SIZE}`);
      if (!auth.isCurrent(started) || sequence !== listSequence) return;
      panel.innerHTML = listing(items) + pager(page, items.length === LIST_PAGE_SIZE, 'application-page');
    } catch (error) {
      if (auth.isCurrent(started) && sequence === listSequence) panel.innerHTML = stateMessage(error.message, 'applications-reload');
    }
  }

  /** 收件箱先检查本人动物，不能用第三方动物 ID 读取联系方式。 */
  async function inbox(id, nextPage = 1) {
    const started = auth.version;
    const current = beginDialogOperation();
    const animal = await auth.privateRequest('/my/animals/' + id);
    if (!auth.isCurrent(started) || !current()) return;
    const items = await auth.privateRequest(`/my/animals/${id}/applications?page=${nextPage}&pageSize=${LIST_PAGE_SIZE}`);
    if (!auth.isCurrent(started) || !current()) return;
    inboxAnimal = animal.id;
    inboxPage = nextPage;
    showDialog(`<h2 id="dialog-title">收到的申请 · ${e(animal.name)}</h2><p>只展示这只动物收到的申请，点击详情查看联系方式。</p>
      ${listing(items, true)}${pager(inboxPage, items.length === LIST_PAGE_SIZE, 'inbox-page')}`, { privateContent: true });
  }

  /** 姓名、电话和微信只在后端授权的详情中显示，默认不建立外部深链。 */
  async function detail(id, incoming = false) {
    const started = auth.version;
    const current = beginDialogOperation();
    const item = await auth.privateRequest('/applications/' + id);
    if (!auth.isCurrent(started) || !current()) return;
    const fields = [['姓名', item.name], ['手机号', item.phone], ['微信号', item.weChat || '未填写'],
      ['居住地', item.residence], ['养宠经验', item.petExperience], ['申请理由', item.reason]];
    const result = item.decisionNote ? `<p class="dialog-note">沟通结果：${e(item.decisionNote)}</p>` : '';
    const decision = incoming && item.status === 'Pending' ? `<section><h3>记录沟通结果</h3>${form('decision',
      '<label>结果<select name="status"><option value="Approved">通过</option><option value="Rejected">拒绝</option></select></label>' +
      `<label>备注（选填）<textarea name="note" maxlength="${FIELD_LIMITS.description}"></textarea></label>`, '保存沟通结果', 'data-id="' + item.id + '"')}</section>` : '';
    showDialog(`<h2 id="dialog-title">领养申请详情</h2><p>动物 #${item.animalId} · ${e(STATUS_LABELS[item.status])}</p>
      <dl class="application-details">${fields.map(([label, value]) => `<dt>${label}</dt><dd class="preserve-lines">${e(value)}</dd>`).join('')}</dl>${result}${decision}
      ${incoming ? '<button class="button" data-action="inbox" data-id="' + item.animalId + '">返回收到的申请</button>' : ''}`, { privateContent: true });
  }

  /** 发布者只记录一次沟通结果，冲突由后端返回且保留表单错误。 */
  async function decide(target) {
    const started = auth.version;
    const current = currentDialogOperation();
    const body = Object.fromEntries(new FormData(target));
    body.note = body.note.trim() || null;
    await auth.privateRequest('/my/applications/' + target.dataset.id + '/decision', { method: 'PUT', body });
    if (!auth.isCurrent(started)) return;
    if (current()) await detail(target.dataset.id, true);
    showToast('沟通结果已保存。');
  }

  /** 退出或换号清空申请及收件箱定位，关闭私人内容由应用统一处理。 */
  function clearPrivate() {
    listSequence++;
    page = 1;
    inboxPage = 1;
    inboxAnimal = null;
    document.querySelector('#my-applications').replaceChildren();
  }
  return { showApply, submit, loadMine, inbox, detail, decide, clearPrivate,
    async nextPage(next) { page = Number(next); await loadMine(); },
    async nextInboxPage(next) { if (inboxAnimal) await inbox(inboxAnimal, Number(next)); } };
}
