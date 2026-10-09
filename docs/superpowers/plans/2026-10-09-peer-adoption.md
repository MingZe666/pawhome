# 直接对接模式实施计划

目标：在 PR #5 中将固定工作人员授权改为账号与动物归属授权，并落实同时上架三只限制。
技术：ASP.NET Core 10、Identity Cookie、EF Core/MySQL；前后端独立，测试/生产环境独立配置。

1. 添加 MarketplaceTests：普通用户同时发布/申请；自己动物申请收件箱；联系方式跨账号隔离；自己动物申请被拒绝；第四只上架冲突、草稿/下架释放名额以及并发最后名额。先执行测试观察新接口 404 等失败。
2. 修改 Data/Entities、DbContext：动物 PublisherId 归属与微信号；切换 IdentityUserContext 去掉角色关系；添加新迁移，旧归属未知档案下架。
3. 复用 AnimalInput.Apply、AnimalView.Projection、ApplicationView.Projection；改用 /api/my/animals 与归属过滤，发布数量检查封装在 PublicationQuota。照片延续现有事务、存储与格式检查，所有写入及未公开照片查询以 PublisherId 授权。
4. 复用账号认证和安全戳；移除 StaffAccountsController、StaffInitializer、BootstrapCommands 与相关测试。更新认证测试为统一账号身份，保留密码恢复、停用会话与 CSRF 回归。
5. 更新 README、API/备份说明、Issue #1–#4 和 PR 描述。执行 Release 构建、SQLite 全套集成测试、真实隔离 MySQL 迁移与测试、模型无待迁移变更检查；推送后检查 GitHub CI，PR 保持未合并。
