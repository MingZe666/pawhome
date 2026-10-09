# 数据库、照片与密钥迁移

## 数据库迁移

迁移保存在 `src/PawHome.Api/Data/Migrations`。部署使用 `migrate` 命令，EF 迁移历史表确保重复执行只应用未执行迁移，不在每次 HTTP 服务启动时自动修改数据库：

```powershell
dotnet run --project backend/src/PawHome.Api --no-launch-profile -- migrate
```

生产建议发布后执行 `dotnet PawHome.Api.dll migrate`，使用具备建表权限的部署连接。日常运行连接只授予业务需要的 CRUD 权限，勿使用 root。

`database/initial.sql` 为初次空库建表 SQL，包含迁移历史记录。仅空库执行一次；重复迁移使用上述命令。官方提供程序生成的所谓幂等脚本包含 MySQL 顶层不接受的条件块，本项目不使用该脚本。生成/检查 SQL：

```powershell
dotnet tool restore
dotnet ef migrations script --project backend/src/PawHome.Api --output backend/database/initial.sql
dotnet ef migrations has-pending-model-changes --project backend/src/PawHome.Api
```

EF 工具从环境变量 `ConnectionStrings__PawHome` 读取连接。生成 SQL 不需要真实数据库；应用迁移需要真实连接。未来新增模型后生成新迁移，先在数据库副本验证，不能修改已部署迁移中的历史字段长度。

## 直接对接模式的历史升级

PeerAdoptionOwnership 迁移增加 Animals.PublisherId 和 Applications.WeChat，删除工作人员角色表及初始化标记。已有账号及密码哈希保留，所有旧登录和邮件令牌通过更新 SecurityStamp 撤销，需要重新登录并重发邮件。

旧动物无法证明归属：保留档案、照片和申请记录，PublisherId 为空且先下架。不会自动分配给任何账号，也没有公开认领接口。新档案归属固定为当前登录账号。旧申请仍允许原申请人查看；只有核验后实际归属的发布者才能接收对应申请。测试版优先用新账号创建虚构动物；如保留旧数据，需停服备份并人工核验归属，确保每账号上架数量不超过三只，再另行设计受控迁移。

应用前先完整备份数据库和照片，并在副本验证。Down 仅恢复旧表结构，不能还原被删除的角色分配、微信号、归属或旧发布状态；回滚应停止服务并恢复部署前备份及对应代码，不能仅执行向下迁移。

## MySQL 导出和恢复

使用 MySQL 8.4 客户端，在维护窗口暂停业务写入，并使用专用备份账号。避免把密码写进参数；`-p` 提示输入，自动任务使用权限受限的客户端选项文件或 secret 注入。

```text
mysqldump -h HOST -u BACKUP_USER -p --single-transaction --routines --triggers --default-character-set=utf8mb4 --result-file=pawhome.sql pawhome
mysql -h TARGET_HOST -u RESTORE_USER -p -e "CREATE DATABASE pawhome CHARACTER SET utf8mb4;"
mysql -h TARGET_HOST -u RESTORE_USER -p pawhome
```

最后一条在客户端内执行 `SOURCE /absolute/path/pawhome.sql;`（使用实际文件路径）。备份包含申请联系方式、账号哈希等私有数据，放在受限目录且加密保存，不上传 GitHub。恢复后核对账号、动物归属、照片/申请数量、迁移历史和抽样业务请求。建议定期备份并实际做一次恢复演练，保留时间按部署要求设置。

## 照片及 Data Protection 密钥

停止写入后备份配置的 `Photos:RootPath` 整个目录；对象键与 AnimalPhotos.StorageKey 必须保持一致。恢复到新目录，修改 `Photos__RootPath` 即可。仅复制照片而不复制数据库会丢失关联关系。

持久化 `Security:KeyPath` 保存 Cookie/邮件令牌使用的 Data Protection 密钥。迁移时：
- 保留密钥和相同应用名 PawHome，可保持有效会话和邮件令牌。
- 不复制密钥，则所有原 Cookie/邮件令牌失效，需要重新登录和重发验证邮件。
- 密钥、邮件测试发件箱与照片均置于静态站点目录以外，限制操作系统目录权限；服务器加密备份密钥。它们都被 .gitignore 排除。

换云服务只需迁移数据库与这些目录并调整连接配置；如接入 OSS，替换 IPhotoStorage 适配器并批量复制对象，保留 StorageKey。API、Identity 和业务授权无需绑定某一云厂商。
