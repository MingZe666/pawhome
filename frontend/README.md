# 爪爪有家前端

原生 HTML/CSS/ES 模块，保留 Sites 页面视觉，动物资料改从真实 API 加载。访客浏览；注册用户可发布自己的动物、申请别人的动物，并在账号中心处理收到的申请。联系方式仅本人和对应发布者可读。

## 先确认代码分支

在仓库根目录检查 `frontend/package.json` 和 `frontend/dev-server.mjs` 是否存在。如果不存在，当前分支尚未包含前端联调代码。可以先切换到已有完整代码的分支：

```powershell
git fetch origin
git switch feat/issue-3-backend
git pull --ff-only
```

前端代码进入 `main` 后，在 `main` 上按相同步骤启动即可。

## 快速启动前端预览

安装 Node.js 22 或更新版本，在仓库根目录打开终端执行：

```powershell
cd frontend
npm ci
npm run dev
```

在尚未配置 HTTPS 证书时，浏览器打开 **http://localhost:5173**。保持终端运行，按 `Ctrl+C` 停止。无需先执行 `npm run build`，也不要直接双击 `index.html`，浏览器模块需要通过 HTTP 服务加载。

这一步启动页面和 API 代理。后端未运行时，动物列表会显示连接失败；注册、登录和提交等完整流程请按下面的 HTTPS 联调步骤启动。

## 完整前后端联调（Windows / PowerShell）

需要 Node.js 22+、.NET 10 SDK、隔离测试 MySQL，后端按 [后端说明](../backend/README.md) 配置并迁移。测试和生产分别使用 `appsettings.Test.json`、`appsettings.Production.json`；数据库连接只进入后端私有配置。

### 第一个终端：准备前端 HTTPS

在仓库根目录打开 PowerShell，复制执行以下完整步骤。证书保存到当前用户的本地应用数据目录；生成的证书密码只在当前终端环境中使用。

```powershell
# 证书保存在仓库外，开发密码随机生成，无需自行填写固定密码。
$taskCertDirectory = Join-Path $env:LOCALAPPDATA 'PawHome\dev-certs'
New-Item -ItemType Directory -Path $taskCertDirectory -Force | Out-Null
$env:PAWHOME_DEV_PFX = Join-Path $taskCertDirectory 'localhost.pfx'
$env:PAWHOME_DEV_CA = Join-Path $taskCertDirectory 'localhost.pem'
$env:PAWHOME_DEV_PFX_PASSWORD = [Guid]::NewGuid().ToString('N')
dotnet dev-certs https --trust
dotnet dev-certs https --export-path "$env:PAWHOME_DEV_PFX" --password "$env:PAWHOME_DEV_PFX_PASSWORD"

# PEM 只含公钥，供 Node 校验后端 HTTPS；兼容 Windows PowerShell 5.1。
$cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($env:PAWHOME_DEV_PFX, $env:PAWHOME_DEV_PFX_PASSWORD)
$pemLineWidth = 64 # PEM 证书每行最多六十四个 Base64 字符。
$certificateBase64 = [Convert]::ToBase64String($cert.RawData)
$certificateBody = ([regex]::Matches($certificateBase64, ".{1,$pemLineWidth}").Value -join "`n")
$certificatePem = "-----BEGIN CERTIFICATE-----`n" + $certificateBody + "`n-----END CERTIFICATE-----`n"
[IO.File]::WriteAllText($env:PAWHOME_DEV_CA, $certificatePem, [Text.UTF8Encoding]::new($false))
```

首次信任开发证书时，Windows 可能显示证书信任确认。保留这个终端，后面的前端启动命令仍在这里运行。

### 第二个终端：启动后端

另开终端，工作目录为仓库根目录。如果后端已经在 `https://localhost:7261` 运行，可跳过此步。先按 [后端说明](../backend/README.md) 设置私有 `ConnectionStrings__PawHome`，再执行：

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Test'
# 数据库连接已在此终端或后端私有配置中设置；首次运行先迁移。
dotnet run --project backend/src/PawHome.Api --no-launch-profile -- migrate
dotnet run --project backend/src/PawHome.Api --no-launch-profile -- --urls https://localhost:7261
```

保持后端终端运行。

### 回到第一个终端：启动前端

```powershell
$env:PAWHOME_API_TARGET = 'https://localhost:7261'
# 在准备证书的同一个终端运行，保留此前三个证书环境变量。
cd frontend
npm ci
npm run dev
```

访问 `https://localhost:5173`。Node 开发服务器只公开浏览器文件和 `assets/`，将 `/api` 转发给后端，保留 Cookie 与多个 Set-Cookie，严格验证后端证书。未配置前端证书时只适合 HTTP 静态预览；完整登录联调使用 HTTPS。端口通过 `PAWHOME_FRONTEND_PORT` 调整。

如果后端端口不同，修改 `PAWHOME_API_TARGET`；前端端口被占用可设置 `$env:PAWHOME_FRONTEND_PORT = '5174'` 后重启，并访问对应端口。新开终端时需重新执行证书准备步骤，因为环境变量不会自动传到另一个终端。

常见问题：

- 没有 `package.json` / `npm run dev` 提示缺少脚本：确认代码分支，并进入 `frontend/` 目录。
- 连接后端失败：检查后端终端仍在运行、目标地址和端口正确、PEM 与后端开发证书一致；不要关闭证书校验。
- HTTP 页面能打开但登录异常：使用完整 HTTPS 步骤，确保浏览器访问的是 `https://localhost:5173`。

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
