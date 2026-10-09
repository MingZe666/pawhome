# 爪爪有家后端测试版

独立 ASP.NET Core 10 Web API，采用 Identity Cookie、EF Core 10 和 MySQL 官方 EF 提供程序，按 Auth、Animals、Applications、Storage 分模块。前端通过同源 /api 接入，运行与联调详见 [前端说明](../frontend/README.md)。

## 账号和业务权限

所有账号由用户自行注册，不设负责人和志愿者角色。一个账号可同时发布自己的动物及申请别人的动物，不能申请自己的动物。发布者只管理自己的档案和照片，并接收对应动物的领养申请；姓名、手机号和微信号等申请资料仅本人及对应发布者可读。

每账号同时上架最多三只动物。草稿不占名额，编辑已上架动物不额外占名额，下架释放名额。创建已上架动物和重新上架均在事务内锁定账号、检查数量，防止并发突破上限。归属由 Cookie 确定，不能通过请求指定或转移。

申请收集姓名、手机号（必填，大陆格式，首期无短信验证）、微信号（选填）、居住地、养宠经验和理由，不收集身份证号。发布者收到信息后可私下联系，并记录通过或拒绝结果；处理结果不自动下架动物。

## 测试与生产配置

使用 .NET 10 SDK、MySQL 8.4。数据库位置不固定，配置依次加载 `appsettings.json`、环境文件和环境变量。

- [appsettings.Test.json](src/PawHome.Api/appsettings.Test.json)：默认固定会话 120 分钟、邮件令牌 60 分钟，邮件写入私有测试发件箱。
- [appsettings.Production.json](src/PawHome.Api/appsettings.Production.json)：SMTP 配置模板，真实连接、邮件凭据和 AllowedHosts 由部署注入。
- 共享配置含照片目录、五 MiB 上传限制、Data Protection 密钥目录及限流设置；环境变量如 `Security__SessionMinutes`、`Photos__RootPath` 可覆盖配置。
- 环境名使用 `ASPNETCORE_ENVIRONMENT=Test` 或 `Production`；启动加 `--no-launch-profile` 避免模板覆盖环境。

仓库根目录 PowerShell 示例（无真实凭据）：

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Test'
$env:ConnectionStrings__PawHome = 'Server=你的数据库地址;Port=3306;Database=pawhome_test;User ID=你的账号;Password=通过安全配置提供;SslMode=Required'
dotnet tool restore
dotnet restore backend/PawHome.slnx --locked-mode
dotnet run --project backend/src/PawHome.Api --no-launch-profile -- migrate
dotnet dev-certs https --trust
dotnet run --project backend/src/PawHome.Api --no-launch-profile -- --urls https://localhost:7261
```

仅保留 `migrate` 数据库命令，无工作人员初始化命令。密码不写入提交的 JSON 或命令行参数；Cookie 要求 HTTPS。测试数据库 TLS 使用 Required，生产可结合可信 CA 使用 VerifyFull。

## 邮件与会话

生产 SMTP 设置 `Mail__Host`、`Mail__Port`、`Mail__UserName`、`Mail__Password`、`Mail__From`；默认 TLS 提交端口 587。文件发件箱只允许测试或开发环境，在静态目录外且无 HTTP 读取入口。

未验证邮箱可登录，邮箱验证后用于密码找回。Identity 生成验证与重置令牌，有效期从配置读取，重置后旧链接和会话失效。Cookie 使用 Secure、HttpOnly、SameSite=Lax，固定有效期不续期，每请求校验启用状态和安全戳。

所有 API 写请求必须先 `GET /api/auth/csrf`，保留 Cookie 并发送 `X-CSRF-TOKEN`；登录或身份变化后重新获取。API 禁止缓存。认证、邮件按来源 IP 限流，测试默认每分钟分别 30 次、5 次，可配置。代理真实 IP 转发由部署任务处理。详见 [API 合同](docs/api.md)。

## 验证与迁移

```powershell
dotnet test backend/PawHome.slnx --configuration Release
dotnet ef migrations has-pending-model-changes --project backend/src/PawHome.Api
```

默认测试用独立 SQLite 数据库、真实 Cookie、私有目录和虚构数据。设置 `PAWHOME_TEST_MYSQL` 后同一套测试使用隔离 MySQL 实例，创建随机数据库并在结束时删除；测试账号需创建/删除测试库权限，不给予生产运行账号。MySQL 通过迁移建库，另有历史数据升级测试。
测试用例按顺序运行，避免 MySql.Data 同步 TLS 握手共享缓存的并发竞态；单个用例中的上架/照片并发请求仍保留，不跳过断言、不关闭 TLS。生产发布前应跟踪驱动修复并验证并发建连，测试隔离不等同于修复第三方驱动。

覆盖认证、CSRF、邮件恢复、固定会话、账号停用、归属隐私、手机号和微信填写规则、申请处理、照片权限及并发上架/上传限制。旧无归属档案升级后下架且保留，旧会话撤销；详见 [迁移备份说明](docs/migration-backup.md)。

## 测试版边界

当前提供 API，不公开部署；生产邮件、阿里云规格、域名和上线由 Issue #4 处理。照片目前存本地目录，可替换 IPhotoStorage 适配器迁移到 OSS。个人发布管理、申请收件箱及联系信息展示由前端任务实现。
