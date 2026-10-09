import { config } from './config.js';
import { createApiClient } from './api.js';
import { createAuth } from './auth.js';
import { createAnimals } from './animals.js';
import { createApplications } from './applications.js';
import { closeDialog, showDialog, showToast, submitOnce } from './ui.js';
import { guides, downloadChecklist, shareSite } from './guides.js';

let auth;
const api = createApiClient(config, {
  onUnauthorized: () => auth.expire(), getSessionVersion: () => auth?.version
});
const dialog = document.querySelector('#info-dialog');
auth = createAuth(api, sessionChanged);
const animals = createAnimals(api, auth);
const applications = createApplications(api, auth);

/** 登录、退出或会话到期时立即清除私人资料及旧弹窗。 */
function sessionChanged(user) {
  animals.clearPrivate();
  applications.clearPrivate();
  if (dialog.open) closeDialog();
  document.querySelector('#account-profile').innerHTML = auth.profile();
  document.querySelector('#account-workspace').hidden = !user;
  document.querySelector('#account-guest').hidden = Boolean(user);
  document.querySelector('#account-button').textContent = user ? '我的账号' : '登录 / 注册';
}

/** 账号中心同时提供发布和领养能力，每次打开刷新真实私人数据。 */
async function openAccount() {
  document.querySelector('#account').hidden = false;
  document.querySelector('#account').scrollIntoView({ behavior: 'smooth' });
  await Promise.all([animals.loadMine(), applications.loadMine()]);
}

/** 集中分发动态按钮，复用现有事件委托且每个模块保持独立职责。 */
async function action(button) {
  const { action, id, mode, page, photo, incoming } = button.dataset;
  const actions = {
    account: () => auth.requireLogin(openAccount),
    auth: () => auth.show(mode),
    logout: () => auth.logout(),
    'public-reload': () => animals.loadPublic(),
    'public-page': () => animals.publicPage(page),
    'my-reload': () => animals.loadMine(),
    'my-page': () => animals.personalPage(page),
    'animal-detail': () => animals.detail(id),
    'animal-new': () => auth.requireLogin(() => animals.editor()),
    'animal-edit': () => auth.requireLogin(() => animals.editor(id)),
    'photo-delete': () => animals.deletePhoto(id, photo),
    apply: () => auth.requireLogin(() => applications.showApply(id)),
    inbox: () => auth.requireLogin(() => applications.inbox(id)),
    'inbox-page': () => applications.nextInboxPage(page),
    'application-detail': () => applications.detail(id, incoming === 'true'),
    'application-page': () => applications.nextPage(page),
    'applications-reload': () => applications.loadMine()
  };
  await actions[action]();
}

// 所有动态按钮复用一次监听；网络错误给出可操作提示。
document.addEventListener('click', async event => {
  try {
    const filter = event.target.closest('[data-filter]');
    if (filter) {
      document.querySelectorAll('[data-filter]').forEach(button => {
        const selected = button === filter;
        button.classList.toggle('selected', selected);
        button.setAttribute('aria-pressed', String(selected));
      });
      await animals.filter(filter.dataset.filter);
    }
    const button = event.target.closest('[data-action]');
    if (button && !button.disabled) await action(button);
    const guide = event.target.closest('[data-dialog]');
    if (guide) showDialog(guides[guide.dataset.dialog]);
    if (event.target.closest('.close-dialog')) closeDialog();
    if (event.target.closest('#download-checklist')) downloadChecklist();
    if (event.target.closest('#share-button')) await shareSite();
  } catch (error) { showToast(error.message); }
});

// 表单共享防重复提交和原位错误提示，不让失败请求伪装为成功。
document.addEventListener('submit', event => {
  const target = event.target.closest('[data-form]');
  if (!target) return;
  event.preventDefault();
  const handlers = { auth: auth.submit, animal: animals.save, photo: animals.upload,
    application: applications.submit, decision: applications.decide };
  void submitOnce(target, () => handlers[target.dataset.form](target));
});

// 原生 Esc 和背景关闭后清除私人表单，不保留验证码及联系方式。
dialog.addEventListener('cancel', event => {
  event.preventDefault();
  closeDialog(); // Esc 当下撤销导航，无需等待排队的 close 事件。
});
dialog.addEventListener('close', () => {
  if (dialog.open) return; // 旧关闭事件到达时，新弹窗可能已打开，不能清空新表单。
  closeDialog(); // 同时撤销仍在加载的弹窗导航。
});
dialog.addEventListener('click', event => {
  if (event.target !== dialog) return;
  const bounds = dialog.getBoundingClientRect();
  const outside = event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom;
  if (outside) closeDialog();
});

/** 邮件跳转可使用 URL 片段预填，片段不会发送给服务器，读取后立即从地址清除。 */
async function start() {
  await Promise.all([animals.loadPublic(), auth.restore()]);
  const match = location.hash.match(/^#(confirm|reset)\?(.*)$/);
  if (match) {
    const values = Object.fromEntries(new URLSearchParams(match[2]));
    history.replaceState(null, '', location.pathname + location.search);
    auth.show(match[1], values);
  }
}
void start();
