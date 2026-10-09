import { showDialog, showToast, escapeHtml } from './ui.js';
/* 指南共用对话框内容入口，避免为相似信息面板重复编写交互逻辑。 */
export const guides = {
  credits: `<h2 id="dialog-title">谢谢镜头里的温柔</h2><p>示例动物照片来自 Unsplash，照片中的动物不代表真实待领养对象。</p><ul><li>首页狗狗：<a class="text-link" href="https://unsplash.com/photos/a-dog-looking-at-the-camera-IraNA9gAt_8" target="_blank" rel="noopener noreferrer">Alexandrina Miclea</a></li><li>猫咪：<a class="text-link" href="https://unsplash.com/photos/orange-tabby-cat-on-wooden-floor-c823JC7oDyA" target="_blank" rel="noopener noreferrer">Mariola Grobelska</a></li><li>小狗：<a class="text-link" href="https://unsplash.com/photos/short-coated-tan-dog-yJOrT_YKS9w" target="_blank" rel="noopener noreferrer">Kylli Kittus</a> · © <a class="text-link" href="https://transly.eu/" target="_blank" rel="noopener noreferrer">Transly</a></li></ul>`,
  checklist: `<h2 id="dialog-title">给新伙伴一个安心的开始</h2><p>在决定领养前，和家人一起确认这些事。</p><ul><li>家人同意领养；租住时已确认房东及居住规则。</li><li>有稳定的陪伴时间，并安排出差、旅行时的照护。</li><li>能够承担食物、日常用品、体检与必要医疗费用。</li><li>家中已做好门窗与阳台安全防护。</li><li>向救助方核实健康、免疫、绝育情况及行为习惯。</li><li>愿意接受适应期，不因搬家、结婚等生活变化而遗弃。</li></ul><p class="dialog-note">领养前请与实际救助方充分沟通，确认见面、协议及后续回访安排。</p><button class="button primary" id="download-checklist">保存准备清单</button>`,
  rescue: `<h2 id="dialog-title">遇见流浪动物，可以这样帮助</h2><p>让自己和动物都处在安全的位置，再采取行动。</p><ul><li><strong>观察情况：</strong>从安全距离观察周边交通、动物状态及是否有主人线索，避免追赶或强行靠近。</li><li><strong>记录信息：</strong>记下准确地点、时间和外观特征，在安全条件下拍照。</li><li><strong>寻找附近帮助：</strong>联系当地正规救助组织或有资质的宠物医院，说明位置和观察到的情况。</li><li><strong>协助寻主：</strong>核实附近走失信息，分享时保护个人信息，不随意公布家庭住址。</li><li><strong>评估能力：</strong>在专业人员指导及自身条件允许时，协助转运、临时安置或寻主。</li></ul><p class="dialog-note">本网站尚未接入现场救助接单服务。这里提供帮助思路；遇到人身或公共安全紧急情况，请联系当地紧急服务。</p>`,
  volunteer: `<h2 id="dialog-title">你的所长，也能成为它的光</h2><p>先了解附近组织的实际需求，选择能持续投入的方式。</p><ul><li><strong>陪伴与照护：</strong>在组织安排下参与喂养、清洁、遛犬和日常陪伴。</li><li><strong>拍照与记录：</strong>帮助整理真实动物档案，拍摄清晰照片、记录日常性格。</li><li><strong>接送协助：</strong>与救助方确认时间、动物状态和安全运输安排。</li><li><strong>临时寄养：</strong>提前确认空间、家人意见、隔离条件、费用及寄养期限。</li><li><strong>信息传播：</strong>核实领养信息和联系渠道后，再分享给可能适合的家庭。</li></ul><p class="dialog-note">目前展示的是参与方式介绍。正式报名需向实际救助组织联系，本网站不会代为提交报名。</p>`
};

/** 将领养准备内容保存到本机，供用户线下与家人、救助方沟通。 */
export function downloadChecklist() {
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
export async function shareSite() {
  const url = `${window.location.origin}${window.location.pathname}#adopt`;
  try {
    await navigator.clipboard.writeText(url);
    showToast('网站链接已复制，可以分享给朋友。');
  } catch {
    // 剪贴板权限可能被浏览器禁止，保留可操作的手动复制方式。
    showDialog(`<h2 id="dialog-title">把温暖分享出去</h2><p>长按或选择下方链接即可复制：</p><p class="dialog-note" style="overflow-wrap:anywhere">${escapeHtml(url)}</p><p>分享前请确认对方可以访问当前网站。</p>`);
  }
}
