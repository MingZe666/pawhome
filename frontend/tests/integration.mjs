import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readdir, readFile, mkdir } from 'node:fs/promises';
import { join } from 'node:path';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.PAWHOME_PLAYWRIGHT_MODULE || 'playwright');
const BASE = process.env.PAWHOME_E2E_BASE || 'https://localhost:5174';
const OUTBOX = process.env.PAWHOME_E2E_OUTBOX;
const OUTPUT = process.env.PAWHOME_E2E_OUTPUT || 'TestResults/frontend';
const PASSWORD = 'FrontendTest!42'; // 公开的虚构测试账号密码，禁止用于部署账号。
const NEW_PASSWORD = 'FrontendChanged!43';
const PHONE = '13800000000'; // 虚构申请人电话。
const WECHAT = 'test_wechat'; // 虚构联系微信。
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l1sAAAAASUVORK5CYII=', 'base64'); // 单像素虚构照片。
const MOBILE = { width: 390, height: 844 }; // 常见手机逻辑像素尺寸。
const WAIT_MS = 15000; // 本地数据库和浏览器单步等待最多十五秒。
const RUN = Date.now().toString(36); // 每次运行使用独立账号，避免覆盖既有数据。

if (!OUTBOX) throw new Error('请指定隔离后端的 PAWHOME_E2E_OUTBOX，禁止读取真实用户邮件。');
await mkdir(OUTPUT, { recursive: true });
const browser = await chromium.launch({ headless: true, channel: process.env.PAWHOME_E2E_CHANNEL || undefined });
const contexts = [];
const pages = [];
const errors = [];

/** 每个人使用独立 Cookie 上下文，始终访问真实 API 而不拦截响应。 */
async function person(viewport) {
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport });
  contexts.push(context);
  const page = await context.newPage();
  page.setDefaultTimeout(WAIT_MS);
  page.on('pageerror', error => errors.push(error.message));
  pages.push(page);
  await page.goto(BASE);
  return { page, context };
}

/** 只读取当前虚构账号邮件，验证码不打印或保存到仓库。 */
async function mail(email, kind) {
  const files = await readdir(OUTBOX);
  const items = await Promise.all(files.filter(file => file.endsWith('.json')).map(async file =>
    JSON.parse(await readFile(join(OUTBOX, file), 'utf8'))));
  const found = items.find(item => item.To === email && item.Kind === kind);
  assert.ok(found, '测试账号应收到相应私有邮件');
  return found;
}

/** 在页面内填表注册及登录，验证 Cookie 能通过同源代理恢复账号。 */
async function register(person, name) {
  const { page } = person;
  person.name = name + RUN;
  person.email = person.name + '@example.test';
  await page.locator('#account-button').click();
  await page.getByRole('button', { name: '注册账号', exact: true }).click();
  await page.getByLabel('账号', { exact: true }).fill(person.name);
  await page.getByLabel('邮箱', { exact: true }).fill(person.email);
  await page.getByLabel('密码', { exact: true }).fill(PASSWORD);
  await page.getByRole('button', { name: '注册', exact: true }).click();
  await page.getByRole('heading', { name: '欢迎回到爪爪有家' }).waitFor();
  await page.getByLabel('账号', { exact: true }).fill(person.name);
  await page.getByLabel('密码', { exact: true }).fill(PASSWORD);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await page.locator('#account-profile').getByText('你好，' + person.name, { exact: true }).waitFor();
  await page.getByRole('button', { name: '发布求助动物', exact: true }).waitFor();
  const cookies = await person.context.cookies();
  const session = cookies.find(cookie => cookie.name === '__Host-PawHome.Session');
  assert.ok(session?.secure && session.httpOnly, 'Cookie 必须真实建立且安全');
}

/** 使用页面与私有测试邮件完成验证，不直接篡改数据库确认状态。 */
async function verify(person) {
  const item = await mail(person.email, 'confirmation');
  const { page } = person;
  await page.getByRole('button', { name: '填写验证码', exact: true }).click();
  await page.getByLabel('邮件中的验证码', { exact: true }).fill(item.Token);
  await page.getByRole('button', { name: '验证邮箱', exact: true }).click();
  await page.locator('#account-profile').getByText(/已验证/).waitFor();
}

/** 创建虚构动物并检查 API 成功响应，返回真实数据库 ID。 */
async function createAnimal(page, name, published) {
  await page.getByRole('button', { name: '发布求助动物', exact: true }).click();
  const form = page.locator('form[data-form="animal"]');
  await form.getByLabel('动物名字', { exact: true }).fill(name);
  await form.getByLabel('种类（猫、狗或其他）', { exact: true }).fill('猫');
  await form.getByLabel('年龄（月）', { exact: true }).fill('6'); // 六个月的虚构动物。
  await form.getByLabel('所在城市', { exact: true }).fill('测试市');
  await form.getByLabel('性格、健康状况与照护说明', { exact: true }).fill('虚构动物，仅供联调。');
  await form.getByLabel('保存后上架', { exact: true }).setChecked(published);
  const waiting = page.waitForResponse(response => response.url().endsWith('/api/my/animals') && response.request().method() === 'POST');
  await form.getByRole('button', { name: '保存档案', exact: true }).click();
  const response = await waiting;
  if (response.status() === 409) { // 三只已上架后第四只应保留表单并明确冲突。
    await form.locator('[data-form-error]').getByText(/最多同时上架/).waitFor();
    return null;
  }
  assert.equal(response.status(), 201); // 创建成功。
  const { id } = await response.json();
  await page.locator('form[data-form="animal"][data-id="' + id + '"]').waitFor();
  return id;
}

/** 原生关闭弹窗后等待消失，避免覆盖下一步的点击。 */
async function close(page) {
  await page.getByRole('button', { name: '关闭对话框', exact: true }).click();
  await page.locator('#info-dialog').waitFor({ state: 'hidden' });
}

/** 完整更新本人动物发布状态，通过页面保存按钮执行真实 PUT。 */
async function publish(page, id, value) {
  await page.locator('#my-animals [data-action="animal-edit"][data-id="' + id + '"]').click();
  const form = page.locator('form[data-form="animal"]');
  await form.getByLabel('保存后上架', { exact: true }).setChecked(value);
  const waiting = page.waitForResponse(response => response.url().endsWith('/api/my/animals/' + id) && response.request().method() === 'PUT');
  await form.getByRole('button', { name: '保存档案', exact: true }).click();
  assert.equal((await waiting).status(), 204); // 更新成功。
  await page.locator('form[data-form="animal"][data-id="' + id + '"]').waitFor();
  await close(page);
}

/** 在公开档案中提交申请，联系人由已确认的字段组成。 */
async function apply(page, id) {
  await page.reload();
  await page.locator('#animal-grid [data-action="animal-detail"][data-id="' + id + '"]').click();
  await page.getByRole('button', { name: '申请领养', exact: true }).click();
  const form = page.locator('form[data-form="application"]');
  await form.getByLabel('姓名', { exact: true }).fill('测试申请人');
  await form.getByLabel('手机号（必填）', { exact: true }).fill(PHONE);
  await form.getByLabel('微信号（选填）', { exact: true }).fill(WECHAT);
  await form.getByLabel('居住地', { exact: true }).fill('虚构住所');
  await form.getByLabel('养宠经验', { exact: true }).fill('虚构经验');
  await form.getByLabel('申请理由', { exact: true }).fill('虚构联调申请');
  const waiting = page.waitForResponse(response => response.url().endsWith('/api/applications') && response.request().method() === 'POST');
  await form.getByRole('button', { name: '提交领养申请', exact: true }).click();
  const response = await waiting;
  assert.equal(response.status(), 201);
  await page.locator('#info-dialog').waitFor({ state: 'hidden' });
  return (await response.json()).id;
}

try {
  const publisher = await person();
  const applicant = await person();
  const outsider = await person();
  const guest = await person();
  await guest.page.locator('.photo-wrap img').evaluate(image => image.decode());
  assert.ok(await guest.page.locator('.photo-wrap img').evaluate(image => image.naturalWidth > 0), '首页原始照片应能解码');
  await register(publisher, 'publisher'); await verify(publisher);
  await register(applicant, 'applicant'); await verify(applicant);
  await register(outsider, 'outsider');
  console.log('通过：真实注册、登录、邮箱验证和安全 Cookie。');

  const first = await createAnimal(publisher.page, '联调小橘', true);
  await publisher.page.getByLabel('动物名字', { exact: true }).fill('尚未保存的名字');
  await publisher.page.locator('form[data-form="photo"] input[name="file"]').setInputFiles({ name: 'test.png', mimeType: 'image/png', buffer: PNG });
  await publisher.page.getByRole('button', { name: '上传照片', exact: true }).click();
  await publisher.page.locator('.photo-list img').waitFor();
  assert.equal(await publisher.page.getByLabel('动物名字', { exact: true }).inputValue(), '尚未保存的名字');
  const photoPath = new URL(await publisher.page.locator('.photo-list img').getAttribute('src'), BASE).pathname;
  assert.equal((await guest.page.request.get(BASE + photoPath)).status(), 200);
  await close(publisher.page);
  const second = await createAnimal(publisher.page, '联调小白', true); await close(publisher.page);
  await createAnimal(publisher.page, '联调小花', true); await close(publisher.page);
  assert.equal(await createAnimal(publisher.page, '联调草稿', true), null);
  const draftForm = publisher.page.locator('form[data-form="animal"]');
  await draftForm.getByLabel('保存后上架', { exact: true }).uncheck();
  const creatingDraft = publisher.page.waitForResponse(response => response.url().endsWith('/api/my/animals') && response.request().method() === 'POST');
  await draftForm.getByRole('button', { name: '保存档案', exact: true }).click();
  const draft = (await (await creatingDraft).json()).id;
  await publisher.page.locator('form[data-form="animal"][data-id="' + draft + '"]').waitFor(); await close(publisher.page);
  await publish(publisher.page, first, false);
  assert.equal((await guest.page.request.get(BASE + photoPath)).status(), 404);
  assert.equal((await outsider.page.request.get(BASE + photoPath)).status(), 404);
  assert.equal((await publisher.page.request.get(BASE + photoPath)).status(), 200);
  await publish(publisher.page, draft, true);
  await publisher.page.locator('#my-animals [data-action="animal-edit"][data-id="' + first + '"]').click();
  await publisher.page.getByLabel('动物名字', { exact: true }).fill('删除照片前的未保存输入');
  await publisher.page.getByRole('button', { name: '删除照片', exact: true }).click();
  await publisher.page.locator('.photo-list img').waitFor({ state: 'hidden' });
  assert.equal(await publisher.page.getByLabel('动物名字', { exact: true }).inputValue(), '删除照片前的未保存输入');
  await close(publisher.page);
  console.log('通过：真实照片上传/删除、草稿权限、第四只冲突及下架释放名额。');

  const applicantAnimal = await createAnimal(applicant.page, '申请人的动物', true); await close(applicant.page);
  const applicationId = await apply(applicant.page, second);
  await apply(publisher.page, applicantAnimal);
  assert.equal((await outsider.page.request.get(BASE + '/api/applications/' + applicationId)).status(), 404);
  assert.equal((await guest.page.request.get(BASE + '/api/applications/' + applicationId)).status(), 401);
  const publicData = await (await guest.page.request.get(BASE + '/api/animals')).text();
  assert.ok(!publicData.includes(PHONE) && !publicData.includes(WECHAT));
  await publisher.page.locator('#account-button').click();
  await publisher.page.locator('#my-animals [data-action="inbox"][data-id="' + second + '"]').click();
  await publisher.page.locator('#dialog-body').getByRole('button', { name: '查看申请', exact: true }).click();
  await publisher.page.locator('.application-details').getByText(PHONE, { exact: true }).waitFor();
  await publisher.page.locator('.application-details').getByText(WECHAT, { exact: true }).waitFor();
  await publisher.page.locator('form[data-form="decision"] textarea[name="note"]').fill('已私下沟通');
  await publisher.page.getByRole('button', { name: '保存沟通结果', exact: true }).click();
  await publisher.page.locator('#dialog-body').getByText(/已通过/).waitFor();
  await close(publisher.page);
  await applicant.page.locator('#account-button').click();
  await applicant.page.locator('#my-applications').getByText(/已通过/).waitFor();
  console.log('通过：同一账号发布/领养，申请收件箱、手机号/微信、结果与跨账号隐私。');

  // 删除会话 Cookie 后触发真实 401，页面必须立即清掉已展示的联系方式。
  await publisher.page.locator('#my-animals [data-action="inbox"][data-id="' + second + '"]').click();
  await publisher.page.locator('#dialog-body').getByRole('button', { name: '查看申请', exact: true }).click();
  await publisher.context.clearCookies({ name: '__Host-PawHome.Session' });
  await publisher.page.getByRole('button', { name: '返回收到的申请', exact: true }).click();
  await publisher.page.locator('#account-workspace').waitFor({ state: 'hidden' });
  assert.ok(!(await publisher.page.locator('body').innerText()).includes(PHONE));
  assert.equal(await publisher.page.locator('#my-applications').innerText(), '');

  // 过期会话点击退出也须清空本地视图，不能停留在已登录界面。
  await outsider.context.clearCookies({ name: '__Host-PawHome.Session' });
  await outsider.page.getByRole('button', { name: '退出登录', exact: true }).click();
  await outsider.page.locator('#account-workspace').waitFor({ state: 'hidden' });

  const staleCookies = await applicant.context.cookies();
  await applicant.page.getByRole('button', { name: '退出登录', exact: true }).click();
  await applicant.page.locator('#account-button').getByText('登录 / 注册', { exact: true }).waitFor();
  await applicant.page.locator('#account-button').click();
  await applicant.page.getByRole('button', { name: '忘记密码', exact: true }).click();
  await applicant.page.getByLabel('邮箱', { exact: true }).fill(applicant.email);
  await applicant.page.getByRole('button', { name: '发送恢复邮件', exact: true }).click();
  await applicant.page.getByRole('heading', { name: '设置新密码' }).waitFor();
  const reset = await mail(applicant.email, 'reset');
  await applicant.page.getByLabel('邮件中的验证码', { exact: true }).fill(reset.Token);
  await applicant.page.getByLabel('新密码', { exact: true }).fill(NEW_PASSWORD);
  await applicant.page.getByRole('button', { name: '重置密码', exact: true }).click();
  await applicant.page.getByRole('heading', { name: '欢迎回到爪爪有家' }).waitFor();
  const stale = await person();
  await stale.context.addCookies(staleCookies);
  assert.equal((await stale.page.request.get(BASE + '/api/auth/me')).status(), 401);
  await applicant.page.getByLabel('账号', { exact: true }).fill(applicant.name);
  await applicant.page.getByLabel('密码', { exact: true }).fill(NEW_PASSWORD);
  await applicant.page.getByRole('button', { name: '登录', exact: true }).click();
  await applicant.page.locator('#account-profile').getByText('你好，' + applicant.name, { exact: true }).waitFor();
  console.log('通过：退出、真实 401 清空私人内容、已验证邮箱恢复及旧 Cookie 撤销。');

  const mobile = await person(MOBILE);
  await mobile.page.locator('#account-button').click();
  await mobile.page.getByLabel('账号', { exact: true }).fill(publisher.name);
  await mobile.page.getByLabel('密码', { exact: true }).fill(PASSWORD);
  await mobile.page.getByRole('button', { name: '登录', exact: true }).click();
  await mobile.page.locator('#account-profile').getByText('你好，' + publisher.name, { exact: true }).waitFor();
  assert.ok(await mobile.page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), '手机不应横向溢出');
  await mobile.page.locator('#account').scrollIntoViewIfNeeded();
  await mobile.page.screenshot({ path: join(OUTPUT, 'mobile-account.png') });
  await guest.page.evaluate(() => window.scrollTo(0, 0)); // 截取首页首屏供审核。
  await guest.page.screenshot({ path: join(OUTPUT, 'desktop-home.png') });
  assert.deepEqual(errors, [], '浏览器不应出现未处理脚本错误');
  console.log('通过：手机账户中心和浏览器无未处理错误。全流程联调通过。');
} catch (error) {
  await Promise.all(pages.map((page, index) => page.screenshot({ path: join(OUTPUT, 'failure-' + index + '.png'), fullPage: true }).catch(() => {})));
  throw error;
} finally {
  await browser.close();
}
