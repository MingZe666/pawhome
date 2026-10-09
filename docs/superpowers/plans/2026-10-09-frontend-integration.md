# 前后端联调实施计划

目标：让现有网站通过真实 C# API 完成已确认的发布和领养流程。
结构：原生 ES 模块，按 API/UI/身份/动物/申请分职责；同源 Cookie 及 CSRF。
技术：浏览器原生模块、Node 内置测试/开发代理、ASP.NET Core 10/MySQL。

## 任务与验证

- [x] frontend/tests/api.test.mjs：先测试凭据、写入 CSRF、FormData、401、校验错误，执行 node --test，确认缺失 API 模块导致失败。
- [x] frontend/api.js、config.js、ui.js：统一协议、转义、弹窗和提示，node --test 通过。
- [x] frontend/auth.js：注册登录、验证/重发/恢复和退出，401 清空状态；复用统一表单处理。
- [x] frontend/animals.js：公开分页与筛选、本人管理、编辑/上下架/照片；frontend/applications.js：申请、本人列表、收件箱和结果。
- [x] frontend/app.js/index.html/styles.css：复用首页与指南，加入账户中心、表单、状态与手机布局；移除示例动物数据。
- [x] frontend/dev-server.mjs：静态资源及 /api 同源代理，HTTPS 证书通过环境配置；Node 实际代理测试验证 Cookie、查询及不可穿越目录。
- [x] frontend/tests/integration.mjs：用真实浏览器、隔离 MySQL、真实 Cookie 和私有测试邮件验证全流程，先确认旧页面没有登录入口。
- [x] README、部署代理示例及 CI：说明配置和运行，不公开测试发件箱；回归后端，独立代码审查并修复实质问题。
- [ ] 提交新 PR，以后端分支为基线，更新 Issue #2 并检查 CI；保持未合并。
