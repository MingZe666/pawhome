/* 示例动物档案是卡片、筛选和详情的唯一数据来源，正式运营时可替换这些记录。 */
const animals = [
  { id: 'xiaomai', name: '小麦', type: 'dog', sex: '♂', age: '约 2 岁', size: '中型犬', place: '杭州', photo: 'assets/hero-dog.jpg', alt: '小麦的示例狗狗照片', tags: ['亲人友好', '喜欢散步'], description: '一只会把快乐写在脸上的小狗，想和你一起走过每个普通日常。', detail: '小麦活泼、愿意亲近人，适合有时间陪伴散步的家庭。实际健康情况、免疫记录和与其他动物相处情况，需要与救助方逐项核实。' },
  { id: 'naitang', name: '奶糖', type: 'cat', sex: '♀', age: '约 1 岁', size: '中华田园猫', place: '杭州', photo: 'assets/cat.jpg', alt: '奶糖的示例猫咪照片', tags: ['安静温柔', '慢慢熟悉'], description: '把阳光和午睡都分给你，想在一个安稳的小家里，慢慢靠近你。', detail: '奶糖需要一点时间适应新环境，适合愿意尊重猫咪边界、耐心陪伴的家庭。带回家前应确认封窗、免疫记录和日常喂养习惯。' },
  { id: 'doubao', name: '豆包', type: 'dog', sex: '♀', age: '约 3 岁', size: '小型犬', place: '杭州', photo: 'assets/small-dog.jpg', alt: '豆包的示例小型狗照片', tags: ['爱陪伴', '体型小巧'], description: '小小的身体，装着大大的爱。希望你的每次回家，都有它迎接。', detail: '豆包喜欢人的陪伴，领养前建议安排见面，了解独处状态、运动需求和生活习惯。照片、年龄及性格描述均用于演示档案展示。' }
];
// 消息展示时长以毫秒计，避免提示过快消失。 
const TOAST_DURATION_MS = 4200;
const dialog = document.querySelector('#info-dialog');
const dialogBody = document.querySelector('#dialog-body');
const grid = document.querySelector('#animal-grid');
const toast = document.querySelector('#toast');
let toastTimer;

/** 根据动物类型渲染档案卡片，同时更新可访问的结果数量提示。 */
function renderAnimals(type = 'all') {
  const visibleAnimals = type === 'all' ? animals : animals.filter(animal => animal.type === type);
  grid.innerHTML = visibleAnimals.map(animal => `
    <article class="animal-card">
      <div class="animal-photo"><img src="${animal.photo}" alt="${animal.alt}" loading="lazy"><span class="animal-badge">等待一个家 · 示例</span></div>
      <div class="animal-body"><div class="animal-name-row"><h3>${animal.name}<span class="sex">${animal.sex}</span></h3><span class="animal-place">${animal.place}</span></div>
      <p class="animal-meta">${animal.age} · ${animal.size}</p><div class="animal-tags">${animal.tags.map(tag => `<span>${tag}</span>`).join('')}</div>
      <p class="animal-description">${animal.description}</p><button class="animal-cta" data-animal="${animal.id}">认识${animal.name}</button></div>
    </article>`).join('');
  document.querySelector('#result-count').textContent = `${visibleAnimals.length} 位可爱的伙伴`;
}

/** 打开统一信息对话框；原生 dialog 自动处理焦点约束和键盘关闭。 */
function showDialog(content) {
  dialogBody.innerHTML = content;
  if (!dialog.open) dialog.showModal();
  dialog.scrollTop = 0; // 每次切换内容从顶部阅读。
}

/** 展示同一来源的完整动物档案，并引导到真实领养前的准备工作。 */
function showAnimal(id) {
  const animal = animals.find(item => item.id === id);
  showDialog(`<img class="dialog-photo" src="${animal.photo}" alt="${animal.alt}"><h2 id="dialog-title">你好，我是${animal.name}</h2><p>${animal.age} · ${animal.size} · ${animal.place}</p><p>${animal.detail}</p><p class="dialog-note">这是示例档案，尚未接入真实救助组织。当前可以了解领养准备；正式申请需在取得救助方联系方式后进行。</p><button class="button primary" data-dialog="checklist">准备迎接新伙伴</button>`);
}

/* 指南共用对话框内容入口，避免为相似信息面板重复编写交互逻辑。 */
const guides = {
  credits: `<h2 id="dialog-title">谢谢镜头里的温柔</h2><p>示例动物照片来自 Unsplash，照片中的动物不代表真实待领养对象。</p><ul><li>首页狗狗：<a class="text-link" href="https://unsplash.com/photos/a-dog-looking-at-the-camera-IraNA9gAt_8" target="_blank" rel="noopener noreferrer">Alexandrina Miclea</a></li><li>猫咪：<a class="text-link" href="https://unsplash.com/photos/orange-tabby-cat-on-wooden-floor-c823JC7oDyA" target="_blank" rel="noopener noreferrer">Mariola Grobelska</a></li><li>小狗：<a class="text-link" href="https://unsplash.com/photos/short-coated-tan-dog-yJOrT_YKS9w" target="_blank" rel="noopener noreferrer">Kylli Kittus</a> · © <a class="text-link" href="https://transly.eu/" target="_blank" rel="noopener noreferrer">Transly</a></li></ul>`,
  checklist: `<h2 id="dialog-title">给新伙伴一个安心的开始</h2><p>在决定领养前，和家人一起确认这些事。</p><ul><li>家人同意领养；租住时已确认房东及居住规则。</li><li>有稳定的陪伴时间，并安排出差、旅行时的照护。</li><li>能够承担食物、日常用品、体检与必要医疗费用。</li><li>家中已做好门窗与阳台安全防护。</li><li>向救助方核实健康、免疫、绝育情况及行为习惯。</li><li>愿意接受适应期，不因搬家、结婚等生活变化而遗弃。</li></ul><p class="dialog-note">领养前请与实际救助方充分沟通，确认见面、协议及后续回访安排。</p><button class="button primary" id="download-checklist">保存准备清单</button>`,
  rescue: `<h2 id="dialog-title">遇见流浪动物，可以这样帮助</h2><p>让自己和动物都处在安全的位置，再采取行动。</p><ul><li><strong>观察情况：</strong>从安全距离观察周边交通、动物状态及是否有主人线索，避免追赶或强行靠近。</li><li><strong>记录信息：</strong>记下准确地点、时间和外观特征，在安全条件下拍照。</li><li><strong>寻找附近帮助：</strong>联系当地正规救助组织或有资质的宠物医院，说明位置和观察到的情况。</li><li><strong>协助寻主：</strong>核实附近走失信息，分享时保护个人信息，不随意公布家庭住址。</li><li><strong>评估能力：</strong>在专业人员指导及自身条件允许时，协助转运、临时安置或寻主。</li></ul><p class="dialog-note">本网站尚未接入现场救助接单服务。这里提供帮助思路；遇到人身或公共安全紧急情况，请联系当地紧急服务。</p>`,
  volunteer: `<h2 id="dialog-title">你的所长，也能成为它的光</h2><p>先了解附近组织的实际需求，选择能持续投入的方式。</p><ul><li><strong>陪伴与照护：</strong>在组织安排下参与喂养、清洁、遛犬和日常陪伴。</li><li><strong>拍照与记录：</strong>帮助整理真实动物档案，拍摄清晰照片、记录日常性格。</li><li><strong>接送协助：</strong>与救助方确认时间、动物状态和安全运输安排。</li><li><strong>临时寄养：</strong>提前确认空间、家人意见、隔离条件、费用及寄养期限。</li><li><strong>信息传播：</strong>核实领养信息和联系渠道后，再分享给可能适合的家庭。</li></ul><p class="dialog-note">目前展示的是参与方式介绍。正式报名需向实际救助组织联系，本网站不会代为提交报名。</p>`
};

/** 用可访问的状态提示反馈复制和下载操作，不模拟外部提交成功。 */
function showToast(message) {
  clearTimeout(toastTimer);
  toast.textContent = message;
  toast.hidden = false;
  toastTimer = setTimeout(() => { toast.hidden = true; }, TOAST_DURATION_MS);
}

/** 将领养准备内容保存到本机，供用户线下与家人、救助方沟通。 */
function downloadChecklist() {
  const content = '爪爪有家 · 领养准备清单\n\n□ 家人同意，房东及居住规则允许\n□ 有稳定陪伴时间和旅行照护安排\n□ 能承担食物、日用品和医疗费用\n□ 门窗与阳台已做好安全防护\n□ 已核实健康、免疫、绝育与行为情况\n□ 已确认救助方的见面、协议与回访要求\n□ 愿意耐心陪伴适应，坚持不遗弃\n\n此清单仅为准备参考，不是领养申请。\n';
  const file = new Blob(['\ufeff', content], { type: 'text/plain;charset=utf-8' });
  const fileUrl = URL.createObjectURL(file);
  const link = document.createElement('a');
  link.href = fileUrl;
  link.download = '爪爪有家-领养准备清单.txt';
  link.click();
  // 延后一轮事件循环释放地址，允许浏览器先启动下载。
  setTimeout(() => URL.revokeObjectURL(fileUrl), 0);
  showToast('准备清单已开始下载，愿每一份爱都有准备。');
}

/** 分享可用的网站地址；复制不可用时提供可直接选择的链接。 */
async function shareSite() {
  const url = `${window.location.origin}${window.location.pathname}#adopt`;
  try {
    await navigator.clipboard.writeText(url);
    showToast('网站链接已复制，可以分享给朋友。');
  } catch {
    // 剪贴板权限可能被浏览器禁止，保留可操作的手动复制方式。
    showDialog(`<h2 id="dialog-title">把温暖分享出去</h2><p>长按或选择下方链接即可复制：</p><p class="dialog-note" style="overflow-wrap:anywhere">${url}</p><p>网站当前为私密预览，分享前请先在 Sites 中调整访问设置。</p>`);
  }
}

// 事件委托复用卡片与对话框动态按钮，避免重渲染时反复绑定监听。
document.addEventListener('click', event => {
  const filter = event.target.closest('[data-filter]');
  if (filter) {
    document.querySelectorAll('[data-filter]').forEach(button => {
      const selected = button === filter;
      button.classList.toggle('selected', selected);
      button.setAttribute('aria-pressed', String(selected));
    });
    renderAnimals(filter.dataset.filter);
  }
  const animal = event.target.closest('[data-animal]');
  if (animal) showAnimal(animal.dataset.animal);
  const guide = event.target.closest('[data-dialog]');
  if (guide) showDialog(guides[guide.dataset.dialog]);
  if (event.target.closest('.close-dialog')) dialog.close();
  if (event.target.closest('#download-checklist')) downloadChecklist();
  if (event.target.closest('#share-button')) shareSite();
});

// 点击对话框外部关闭；内容区域的点击继续保留对话框。
dialog.addEventListener('click', event => {
  if (event.target !== dialog) return;
  const bounds = dialog.getBoundingClientRect();
  const outside = event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom;
  if (outside) dialog.close();
});
renderAnimals();

