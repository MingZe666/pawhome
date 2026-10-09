import { beginDialogOperation, currentDialogOperation, escapeHtml as e, form, input, FIELD_LIMITS, LIST_PAGE_SIZE, pager, showDialog, showToast, stateMessage, textarea } from './ui.js';

const MAX_AGE_MONTHS = 600; // 与后端最大五十年年龄一致。
const MAX_PHOTOS = 10; // 与后端每动物十张照片上限一致。
const PHOTO_MAX_BYTES = 5 * 1024 * 1024; // 默认上传上限五 MiB，部署调整仍以后端为准。

/** 动物公共展示及本人发布管理，共用现有卡片和详情视觉。 */
export function createAnimals(api, auth) {
  let page = 1; // 页码从一开始。
  let myPage = 1;
  let species = '';
  let publicSequence = 0; // 避免快速切换筛选时旧响应覆盖新结果。
  let openEditorId = null;
  let mySequence = 0; // 同一账号的私人分页也丢弃旧响应。

  /** 卡片只显示动物资料，照片使用后端受控 URL，没有申请联系方式。 */
  function card(animal, personal = false) {
    const photo = animal.photos[0];
    const image = photo ? `<img src="${e(api.photoUrl(photo.url))}" alt="${e(animal.name)}的照片" loading="lazy">`
      : '<div class="photo-placeholder" aria-label="暂无动物照片">🐾<span>照片待补充</span></div>';
    return `<article class="animal-card" data-animal-id="${animal.id}">
      <div class="animal-photo">${image}<span class="animal-badge">${animal.isPublished ? '等待一个家' : '草稿 / 已下架'}</span></div>
      <div class="animal-body"><div class="animal-name-row"><h3>${e(animal.name)}<span class="sex">${e(animal.sex)}</span></h3><span class="animal-place">${e(animal.city)}</span></div>
      <p class="animal-meta">${animal.ageMonths} 个月 · ${e(animal.species)}</p><p class="animal-description">${e(animal.description)}</p>
      ${personal ? `<div class="card-actions"><button class="animal-cta" data-action="animal-edit" data-id="${animal.id}">编辑档案与照片</button>
      <button class="button" data-action="inbox" data-id="${animal.id}">收到的申请</button></div>`
      : `<button class="animal-cta" data-action="animal-detail" data-id="${animal.id}">认识${e(animal.name)}</button>`}</div></article>`;
  }

  /** 服务端在分页之前筛选，连接失败与无动物分别明确显示。 */
  async function loadPublic() {
    const sequence = ++publicSequence;
    const grid = document.querySelector('#animal-grid');
    grid.innerHTML = stateMessage('正在寻找伙伴…');
    document.querySelector('#public-pager').replaceChildren();
    document.querySelector('#result-count').textContent = '';
    try {
      const data = await api.request(`/animals?page=${page}&pageSize=${LIST_PAGE_SIZE}&species=${encodeURIComponent(species)}`);
      if (sequence !== publicSequence) return;
      grid.innerHTML = data.length ? data.map(animal => card(animal)).join('') : stateMessage('这里暂时没有上架的伙伴，过段时间再来看看。');
      document.querySelector('#result-count').textContent = `本页 ${data.length} 位伙伴`;
      document.querySelector('#public-pager').innerHTML = pager(page, data.length === LIST_PAGE_SIZE, 'public-page');
    } catch (error) {
      if (sequence === publicSequence) grid.innerHTML = stateMessage(error.message, 'public-reload');
    }
  }

  /** 本人分页展示包含草稿，切换身份时丢弃旧请求结果。 */
  async function loadMine() {
    if (!auth.user) return;
    const started = auth.version;
    const sequence = ++mySequence;
    const panel = document.querySelector('#my-animals');
    panel.innerHTML = stateMessage('正在加载你的动物…');
    try {
      const data = await auth.privateRequest(`/my/animals?page=${myPage}&pageSize=${LIST_PAGE_SIZE}`);
      if (!auth.isCurrent(started) || sequence !== mySequence) return;
      panel.innerHTML = `<p class="field-hint">最多同时上架 3 只动物。草稿不占名额，下架后可上架另一只。</p>
        <div class="animal-grid">${data.length ? data.map(animal => card(animal, true)).join('') : stateMessage('还没有动物档案，可以先创建一个草稿。')}</div>
        ${pager(myPage, data.length === LIST_PAGE_SIZE, 'my-page')}`;
    } catch (error) {
      if (auth.isCurrent(started) && sequence === mySequence) panel.innerHTML = stateMessage(error.message, 'my-reload');
    }
  }

  /** 详情重新查状态；本人动物直接提供管理入口，其他动物引导申请。 */
  async function detail(id) {
    const current = beginDialogOperation();
    const started = auth.version;
    const animal = await api.request('/animals/' + id);
    if (!current() || started !== auth.version) return;
    let own = false;
    if (auth.user) {
      try { await auth.privateRequest('/my/animals/' + id); own = true; }
      catch (error) { if (error.status !== 404) throw error; } // 非本人动物的 404 属于正常归属检查。
    }
    if (!current() || started !== auth.version) return;
    const photos = animal.photos.map(photo => `<img class="dialog-photo" src="${e(api.photoUrl(photo.url))}" alt="${e(animal.name)}的照片">`).join('');
    showDialog(`${photos}<h2 id="dialog-title">你好，我是${e(animal.name)}</h2><p>${animal.ageMonths} 个月 · ${e(animal.species)} · ${e(animal.city)}</p>
      <p class="preserve-lines">${e(animal.description)}</p><p class="dialog-note">申请后，发布者会查看你填写的联系方式，双方可私下沟通领养安排。</p>
      <button class="button primary" data-action="${own ? 'animal-edit' : 'apply'}" data-id="${animal.id}">${own ? '管理我的动物' : '申请领养'}</button>`);
  }

  /** 创建或编辑本人档案，保存后才能上传照片，所有归属仍由后端校验。 */
  async function editor(id) {
    const current = beginDialogOperation();
    const started = auth.version;
    const animal = id ? await auth.privateRequest('/my/animals/' + id) : null;
    if (!auth.isCurrent(started) || !current()) return;
    openEditorId = animal?.id ?? null;
    const fields = input('name', '动物名字', { value: animal?.name, maxLength: FIELD_LIMITS.name }) +
      input('species', '种类（猫、狗或其他）', { value: animal?.species || '猫', maxLength: FIELD_LIMITS.shortText }) +
      input('sex', '性别', { value: animal?.sex || '未知', maxLength: FIELD_LIMITS.shortText }) +
      `<label>年龄（月）<input name="ageMonths" type="number" min="0" max="${MAX_AGE_MONTHS}" value="${animal?.ageMonths ?? 0}" required></label>` +
      input('city', '所在城市', { value: animal?.city, maxLength: FIELD_LIMITS.address }) +
      textarea('description', '性格、健康状况与照护说明', animal?.description) +
      `<label class="checkbox-label"><input name="isPublished" type="checkbox" ${animal?.isPublished ? 'checked' : ''}>保存后上架</label>
      <p class="field-hint">不勾选会保存为草稿或下架。每账号最多同时上架 3 只。</p>`;
    const photos = animal ? photoEditor(animal) : '<p class="field-hint">先保存档案，再添加照片。</p>';
    showDialog(`<h2 id="dialog-title">${animal ? '编辑动物档案' : '发布求助动物'}</h2>${form('animal', fields, '保存档案', animal ? 'data-id="' + animal.id + '"' : '')}${photos}`, { privateContent: true });
  }

  /** 照片区域独立渲染，照片操作不会覆盖未保存的档案输入。 */
  function photoEditor(animal) {
    return `<section class="photo-editor"><h3>动物照片</h3><div class="photo-list">${animal.photos.map(photo =>
      `<div><img src="${e(api.photoUrl(photo.url))}" alt="${e(animal.name)}的照片"><button type="button" class="text-link" data-action="photo-delete" data-id="${animal.id}" data-photo="${photo.id}">删除照片</button></div>`).join('')}</div>
      ${animal.photos.length < MAX_PHOTOS ? form('photo', '<label>选择照片<input name="file" type="file" accept="image/jpeg,image/png,image/webp" required></label><p class="field-hint">JPEG、PNG 或 WebP，默认最多 5 MiB，每动物最多 10 张。</p>', '上传照片', 'data-id="' + animal.id + '"')
        : '<p>照片已达十张上限，请先删除不需要的照片。</p>'}</section>`;
  }

  /** 只更新仍在当前账号与当前弹窗中的照片区域。 */
  async function refreshPhotos(id, started, current) {
    const animal = await auth.privateRequest('/my/animals/' + id);
    if (!auth.isCurrent(started) || !current() || String(openEditorId) !== String(id)) return;
    document.querySelector('.photo-editor').outerHTML = photoEditor(animal);
  }

  /** 保存档案后同步本人和公开列表，创建成功打开同一编辑器继续补照片。 */
  async function save(target) {
    const started = auth.version;
    const current = currentDialogOperation();
    const data = Object.fromEntries(new FormData(target));
    data.ageMonths = Number(data.ageMonths);
    data.isPublished = target.elements.isPublished.checked;
    const id = target.dataset.id;
    const result = await auth.privateRequest('/my/animals' + (id ? '/' + id : ''), { method: id ? 'PUT' : 'POST', body: data });
    if (!auth.isCurrent(started)) return;
    await Promise.all([loadMine(), loadPublic()]);
    if (!auth.isCurrent(started)) return;
    if (current()) await editor(id || result.id);
    showToast('档案已保存。');
  }

  /** 上传由统一请求封装处理 multipart 防伪，客户端仅做有意义的大小提示。 */
  async function upload(target) {
    const started = auth.version;
    const current = currentDialogOperation();
    const file = target.elements.file.files[0];
    if (file.size > PHOTO_MAX_BYTES) throw new Error('照片超过默认 5 MiB 上限，请压缩后上传。');
    const body = new FormData();
    body.append('file', file);
    await auth.privateRequest('/my/animals/' + target.dataset.id + '/photos', { method: 'POST', body });
    if (!auth.isCurrent(started)) return;
    await Promise.all([loadMine(), loadPublic(), refreshPhotos(target.dataset.id, started, current)]);
    showToast('照片已上传。');
  }

  /** 只删除当前账号的照片，再刷新弹窗和卡片。 */
  async function deletePhoto(id, photoId) {
    const started = auth.version;
    const current = currentDialogOperation();
    await auth.privateRequest(`/my/animals/${id}/photos/${photoId}`, { method: 'DELETE' });
    if (!auth.isCurrent(started)) return;
    await Promise.all([loadMine(), loadPublic(), refreshPhotos(id, started, current)]);
    showToast('照片已删除。');
  }

  /** 身份变化清空草稿缓存和私人页面。 */
  function clearPrivate() {
    mySequence++;
    openEditorId = null;
    myPage = 1;
    document.querySelector('#my-animals').replaceChildren();
  }

  return { loadPublic, loadMine, detail, editor, save, upload, deletePhoto, clearPrivate,
    async filter(type) { species = { cat: '猫', dog: '狗', all: '' }[type]; page = 1; await loadPublic(); },
    async publicPage(next) { page = Number(next); await loadPublic(); },
    async personalPage(next) { myPage = Number(next); await loadMine(); } };
}
