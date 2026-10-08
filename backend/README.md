# 爪爪有家后端测试版

独立 ASP.NET Core 10 Web API，使用 Identity + Cookie、EF Core 10 和 MySQL 官方 EF 提供程序。模块化单体按 Auth、Animals、Applications、Storage 分目录。前端仍是独立静态演示，接入后端属于 Issue #2。

## 环境配置

使用 .NET 10 SDK、MySQL 8.4。数据库部署位置不固定，连接信息完全由配置决定。配置依次加载 `appsettings.json`、`appsettings.{环境}.json` 和环境变量，后者覆盖前者。

- [appsettings.Test.json](src/PawHome.Api/appsettings.Test.json)：测试环境，固定会话默认 120 分钟、邮件验证/重置令牌默认 60 分钟。邮件写入私有目录 `App_Data/outbox`，不发送真实邮件。
- [appsettings.Production.json](src/PawHome.Api/appsettings.Production.json)：生产环境，SMTP 邮件。上线必须设置真实连接、SMTP 和 AllowedHosts；仓库只保留空凭据模板。
- 共用配置包含照片位置、五 MiB 上传限制、密钥目录及限流窗口。环境变量例如 `Security__SessionMinutes`、`Photos__RootPath`，数字单位以选项类注释为准。
- 环境名使用 `ASPNETCORE_ENVIRONMENT=Test` 或 `Production`。本地启动加 `--no-launch-profile`，避免模板启动配置覆盖环境。

PowerShell 示例（在仓库根目录；示例不包含真实密码）：

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Test'
$env:ConnectionStrings__PawHome = 'Server=你的数据库地址;Port=3306;Database=pawhome_test;User ID=你的账号;Password=通过安全配置提供;SslMode=Required'
dotnet restore backend/PawHome.slnx
dotnet run --project backend/src/PawHome.Api --no-launch-profile -- migrate
dotnet dev-certs https --trust
dotnet run --project backend/src/PawHome.Api --no-launch-profile -- --urls https://localhost:7261
```

`SslMode` 按数据库实际 TLS 配置选择。测试实例使用 `Required`，生产可配合可信 CA 使用 `VerifyFull`。密码不写进提交的 JSON，不通过命令行参数传入。默认安全 Cookie 要求 HTTPS。

## 首个主负责人及其他负责人

先执行 `migrate`，随后在可交互的服务器终端运行：

```powershell
dotnet run --project backend/src/PawHome.Api --no-launch-profile -- bootstrap-owner --user-name owner --email owner@example.test
dotnet run --project backend/src/PawHome.Api --no-launch-profile -- add-manager --user-name manager --email manager@example.test
```

密码通过不回显的键盘输入设置，无默认密码。账号已有 Owner 时重复初始化返回非零退出码且不改原账号；初始化标记、账号和角色在同一事务提交，两个终端并行运行也只允许一个成功。没有 Owner 时不允许添加 Manager。没有任何 HTTP 初始化或自助授予角色端点。通过服务器访问权限限制 CLI 使用。初始化账号自动发送确认邮件，邮箱验证后才能找回密码。

Owner（主负责人）和 Manager（负责人）权限相同，可管理动物、查看/审核申请、创建/停用志愿者。Volunteer 仅管理动物，申请接口拒绝其访问。普通账号无工作人员角色，只能提交及查看自己申请。新注册不能指定角色。手机号只校验大陆手机号格式，首期不做短信验证，不收集身份证号。

## 邮件与会话

生产 SMTP 设置 `Mail__Host`、`Mail__Port`、`Mail__UserName`、`Mail__Password`、`Mail__From`；默认端口 587，通过 TLS 提交。测试发件箱不在静态网站目录内，无读取 HTTP 接口，只允许开发/测试使用。邮件中提供验证/恢复操作信息；前端页面接入参见 [API 合同](docs/api.md)。

未验证邮箱可以登录，只有验证后的邮箱可找回密码。令牌有效期配置化；密码重置后旧令牌不能再用。Cookie 使用 Secure、HttpOnly、SameSite=Lax，固定有效期，不因访问续期。每次请求校验账号启用状态、角色和 Identity 安全戳，停用/角色改变/重置密码后的旧 Cookie 立即失效。

所有 API 写请求（包括注册和登录）必须先获取 `GET /api/auth/csrf`，保留响应 Cookie，并发送 `X-CSRF-TOKEN`。登录或身份变化后重新获取令牌。所有 API 响应禁止缓存。认证和邮件端点按来源 IP 限流；测试默认认证 30 次/分钟，邮件 5 次/分钟，可配置。代理部署的真实 IP 转发策略及对外滥用防护由部署任务设置。

## 验证

```powershell
dotnet test backend/PawHome.slnx
```

默认集成测试使用每个测试独立的 SQLite 关系数据库、Cookie、私有目录和虚构数据。真实 MySQL 验证可为隔离测试实例设置 `PAWHOME_TEST_MYSQL` 连接字符串再运行同一命令；测试创建随机命名数据库并在结束时删除，账号必须有创建/删除这些测试库的权限。此权限只用于测试，不给予生产服务账号。MySQL 测试通过 EF 迁移初始化，不使用 EnsureCreated 绕过迁移。

覆盖认证、CSRF、固定会话、验证/密码恢复、会话撤销、角色和本人申请边界、审核并发状态保护、照片签名和发布权限。数据库迁移及备份/恢复见 [迁移说明](docs/migration-backup.md)。

## 测试版边界

本 PR 提供可运行后台和 API 文档，不公开部署，不把静态前端变为完整业务前端。生产 SMTP 凭据、服务商、域名与公开上线由 Issue #4 处理。照片适配器目前使用本地目录；更换 OSS 等存储只需替换 IPhotoStorage 实现并迁移对象键。账号管理和审核页面属于前端任务。
