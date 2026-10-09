# 爪爪有家前端

原生 HTML/CSS/ES 模块，保留 Sites 页面视觉，动物资料改从真实 API 加载。访客浏览；注册用户可发布自己的动物、申请别人的动物，并在账号中心处理收到的申请。联系方式仅本人和对应发布者可读。

## 本地联调

需要 Node.js 22+、.NET 10 SDK、隔离测试 MySQL，后端按 [后端说明](../backend/README.md) 配置并迁移。测试和生产分别使用 `appsettings.Test.json`、`appsettings.Production.json`；数据库连接只进入后端私有配置。

1. 在仓库根目录配置后端环境变量并运行 Test 后端，默认地址 `https://localhost:7261`。
2. 将受信任的 localhost 开发证书导出到仓库外的私有目录。PowerShell 示例：

```powershell
# 请替换路径；证书密码通过私有环境变量提供，不提交仓库。
dotnet dev-certs https --trust
dotnet dev-certs https --export-path "$env:PAWHOME_DEV_PFX" --password "$env:PAWHOME_DEV_PFX_PASSWORD"
# PEM 只含公钥证书，供 Node 严格验证上游 HTTPS。
$cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($env:PAWHOME_DEV_PFX, $env:PAWHOME_DEV_PFX_PASSWORD)
[IO.File]::WriteAllText($env:PAWHOME_DEV_CA, $cert.ExportCertificatePem())
```

3. 在 `frontend/` 目录运行：

```powershell
$env:PAWHOME_API_TARGET = 'https://localhost:7261'
# PAWHOME_DEV_PFX、PAWHOME_DEV_PFX_PASSWORD、PAWHOME_DEV_CA 已设置为私有证书路径与密码。
npm ci
npm run dev
```

访问 `https://localhost:5173`。Node 开发服务器只公开浏览器文件和 `assets/`，将 `/api` 转发给后端，保留 Cookie 与多个 Set-Cookie，严格验证后端证书。未配置前端证书时只适合 HTTP 静态预览；完整登录联调使用 HTTPS。端口通过 `PAWHOME_FRONTEND_PORT` 调整。

`config.js` 的 `apiBase` 默认 `/api`，必须保持同源。前端不保存密码、Cookie、邮件令牌或联系人到 localStorage。写请求每次获取新 CSRF，不自动重试。退出或会话失效清空私人视图，迟到响应不能覆盖新账号或新弹窗。

## 邮箱测试

Test 后端使用 `Mail.Provider=File`，邮件放在后端私有发件箱。注册后可填邮件中的 userId/token 验证邮箱；已验证邮箱可找回密码。发件箱必须位于静态目录外，没有浏览器读取接口。生产替换成 SMTP，短信和微信登录不在首期范围。

## 验证

```powershell
npm test
npm run build
npx playwright install chromium
# 指向专用 Test 后端的私有发件箱，仅允许虚构账号。
$env:PAWHOME_E2E_OUTBOX = '你的私有测试发件箱绝对路径'
$env:PAWHOME_E2E_BASE = 'https://localhost:5174'
npm run test:integration
```

浏览器测试运行前需要后端及 HTTPS 开发代理已监听该地址；可用 `PAWHOME_E2E_CHANNEL=msedge` 复用本机 Edge。测试创建独立虚构账号和动物，验证真实 Cookie、邮件、上传、三只限额、草稿/联系权限、同账号两种能力、恢复密码、旧 Cookie 失效、照片操作保留未保存输入及手机布局。不要对真实用户数据库运行。默认截图输出 `frontend/TestResults/frontend`，`PAWHOME_E2E_OUTPUT` 可调整。CI 自动创建临时 MySQL 和证书并运行联调。

## 生产发布与模块

`npm run build` 生成只含浏览器文件和图片的 `dist/`。前端与 C# 服务独立发布，在同一 HTTPS 域名下将 `/api` 代理到后端；不要把整个仓库、`node_modules`、发件箱、照片磁盘目录或 Data Protection 密钥作为静态根目录。

- [Nginx 示例](deploy/nginx.conf.example)：域名和证书路径需替换，上游使用 HTTPS 且严格验证后端证书。
- [IIS 示例](deploy/web.config.example)：需安装 URL Rewrite 与 ARR、启用 ARR Proxy，在 HTTPS 站点下部署 dist 并配置后端监听。
- `api.js`：Cookie、CSRF、上传及错误合同；`ui.js`：转义、表单、弹窗版本和提示。
- `auth.js`：账号状态和邮件流程；`animals.js`：公开及本人动物/照片；`applications.js`：申请和收件箱。
- `app.js`：组合模块与事件；`guides.js`：原有指南；`assets/`：原页面素材，动物动态照片由后端提供。

示例不执行公开部署。生产域名、邮件服务、可信代理/IP 限流与阿里云规格由 Issue #4 处理。图片作者致谢保留在页面中。
当前后端未采纳转发头，示例保留 HTTPS 上游使防伪检查有效；不能直接改成 HTTP。若后续做 TLS 终止，应先配置可信代理及转发头。代理 IP 限流需在正式部署时完善。
