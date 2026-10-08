# REST API 合同

JSON 使用 camelCase，枚举使用字符串。错误返回标准 ProblemDetails / ValidationProblemDetails；401 未认证，403 权限不足，404 资源不存在或不属于自己，409 重复申请或审核冲突，429 限流。Cookie 登录不返回 bearer token。

所有写入（POST/PUT/DELETE）必须包含 `X-CSRF-TOKEN`。先使用同一浏览器访问 `GET /api/auth/csrf`，从 JSON `token` 读取令牌；保留服务器设置的 Cookie。登录前后令牌身份不同，登录后重新获取。推荐部署时将前端静态目录与 `/api` 通过同一 HTTPS 域名代理；前后端代码仍独立。本版不开放任意跨域 Cookie 调用。

## 账号

| 方法/路径 | 请求/说明 |
|---|---|
| GET /api/auth/csrf | 匿名可用，返回 { token } |
| POST /api/auth/register | { userName, email, password }，返回 201 与安全账号资料；自动发送确认邮件 |
| POST /api/auth/login | { userName, password }，返回 200 与 Cookie |
| GET /api/auth/me | 登录后账号资料及角色 |
| POST /api/auth/logout | 退出，返回 204 |
| POST /api/auth/confirm-email | { userId, token }，成功 204 |
| POST /api/auth/resend-confirmation | { email }，始终返回统一 202 |
| POST /api/auth/forgot-password | { email }，符合条件时向已验证邮箱发邮件，统一 202 |
| POST /api/auth/reset-password | { email, token, newPassword }，成功 204，旧令牌及旧会话失效 |

密码至少十个字符，并包含大小写字母、数字和符号。连续五次错误密码锁定五分钟。注册为普通账号；提交的 roles 等额外字段不能提升权限。邮件内容含 Identity 操作令牌，按原始字符串提交（如果通过链接传递必须 URL 编码，不能改变加号等字符）。不在日志或公共页面暴露令牌。

## 动物与照片

列表分页 `?page=1&pageSize=20`，单页最多 100 条。公开列表仅返回已发布动物，公开详情的未发布档案返回 404。

| 方法/路径 | 权限/请求 |
|---|---|
| GET /api/animals | 访客，公开列表 |
| GET /api/animals/{id} | 访客，公开详情 |
| GET /api/staff/animals | 所有工作人员，含未发布档案 |
| POST /api/staff/animals | 所有工作人员，创建档案 |
| PUT /api/staff/animals/{id} | 所有工作人员，完整更新/上下架 |
| POST /api/staff/animals/{id}/photos | 所有工作人员，multipart 字段 file |
| DELETE /api/staff/animals/{id}/photos/{photoId} | 所有工作人员，删除照片 |
| GET /api/animals/{id}/photos/{photoId} | 发布照片公开；未发布照片只限动物管理员 |

动物写入示例：

```json
{
  "name": "测试小橘",
  "species": "猫",
  "sex": "母",
  "ageMonths": 6,
  "city": "测试市",
  "description": "虚构的测试动物",
  "isPublished": true
}
```

上传接受 JPEG/PNG/WebP，检查大小、MIME 与二进制签名，禁止 SVG，默认五 MiB、每动物十张。忽略客户端文件名并生成随机对象键；响应照片 URL，不暴露磁盘路径。动物下架后图片不再公开。完整解码、压缩和内容审查不在本版实现内。

## 领养申请

| 方法/路径 | 权限/说明 |
|---|---|
| POST /api/applications | 登录普通用户/负责人，已发布动物，同一账号每动物一次 |
| GET /api/applications/mine | 本人申请列表，志愿者拒绝 |
| GET /api/applications/{id} | 本人详情；他人 ID 返回 404，志愿者拒绝 |
| GET /api/staff/applications | Owner/Manager，所有申请与必要联系方式 |
| PUT /api/staff/applications/{id}/decision | Owner/Manager，{ status: "Approved" 或 "Rejected", note } |

申请示例仅用虚构信息：

```json
{
  "animalId": 1,
  "name": "测试申请人",
  "phone": "13800000000",
  "residence": "测试住所",
  "petExperience": "测试养宠经验",
  "reason": "测试申请理由"
}
```

申请字段不含身份证号。服务端从登录 Cookie 确定申请人和创建时间，不接受客户端替换 applicantId/status。Pending 只能原子更新为 Approved/Rejected；已处理不能覆盖，返回 409。通过申请不自动下架动物，由工作人员另行操作动物发布状态。

## 志愿者

Owner/Manager 可 `GET /api/staff/volunteers?page=1&pageSize=20` 分页查看志愿者账号与启用状态；`POST /api/staff/volunteers` 请求同注册字段，角色固定为 Volunteer 并自动发送邮箱确认邮件。可 `PUT /api/staff/volunteers/{id}/disable` 停用纯志愿者账号，禁止停用本人及其他负责人。账号禁用后新登录和旧会话均被拒绝。不提供面向普通用户的角色变更或负责人创建 API。

## 开发接口文档

Test/Development 环境提供 `GET /openapi/v1.json`。生产环境不暴露 OpenAPI。可使用生成文档在 API 客户端导入请求；浏览器测试仍需 Cookie 和 CSRF，不提供绕过授权的测试接口。
