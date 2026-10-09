# REST API 合同

JSON 使用 camelCase，枚举用字符串，Cookie 登录不返回 bearer token。错误使用 ProblemDetails / ValidationProblemDetails；401 未登录，404 不存在或不属于本人，409 名额不足、重复申请或处理冲突，429 限流。

所有写入（POST/PUT/DELETE）需要 `X-CSRF-TOKEN`：同一浏览器先获取 `GET /api/auth/csrf` 的 token 并保留 Cookie，登录后重新获取。建议同一 HTTPS 域名代理前端和 /api，源代码仍分离，不开放任意跨域 Cookie 调用。

## 账号

所有账号权限相同，能力由资源归属决定，同一人可发布和申请。

| 方法/路径 | 请求与说明 |
|---|---|
| GET /api/auth/csrf | 匿名可用，返回 { token } |
| POST /api/auth/register | { userName, email, password }，201，发送验证邮件 |
| POST /api/auth/login | { userName, password }，200，与安全 Cookie |
| GET /api/auth/me | 本人 { id, userName, email, emailConfirmed }，无角色字段 |
| POST /api/auth/logout | 204 |
| POST /api/auth/confirm-email | { userId, token }，204 |
| POST /api/auth/resend-confirmation | { email }，统一 202 |
| POST /api/auth/forgot-password | { email }，向已验证邮箱发送邮件，统一 202 |
| POST /api/auth/reset-password | { email, token, newPassword }，204，撤销旧令牌/会话 |

密码至少十字符，含大小写、数字、符号；连续五次错误锁定五分钟。未验证邮箱可登录，验证后可恢复密码。令牌原样提交，链接中必须 URL 编码，不能改变加号。额外的 roles 字段无效，没有工作人员账号 API 或初始化命令。

## 动物与照片

列表分页 `?page=1&pageSize=20`，每页最多 100 条。
公开动物列表支持可选 `species=猫` 或 `species=狗`（精确匹配，最长 32 字符），服务端先筛选再分页；不传或为空展示全部种类。

| 方法/路径 | 权限/说明 |
|---|---|
| GET /api/animals | 访客，只列已上架档案 |
| GET /api/animals/{id} | 访客，未上架返回 404 |
| GET /api/my/animals | 登录，只列自己档案，含草稿/已下架 |
| GET /api/my/animals/{id} | 登录，本人档案详情 |
| POST /api/my/animals | 登录，归属固定为当前账号，201 与 { id } |
| PUT /api/my/animals/{id} | 本人完整更新及上下架，204 |
| POST /api/my/animals/{id}/photos | 本人，multipart 字段 file |
| DELETE /api/my/animals/{id}/photos/{photoId} | 本人，删除元数据及存储对象 |
| GET /api/animals/{id}/photos/{photoId} | 已上架公开，草稿/下架仅发布者本人 |

每账号同时上架最多 **3 只**。草稿不占名额，下架释放名额；创建及更新发布状态均检查并发名额，超限返回 409。编辑现有已上架档案不额外占名额。不能通过请求改变 publisherId。

动物请求示例（虚构数据）：

```json
{
  "name": "测试小橘",
  "species": "猫",
  "sex": "母",
  "ageMonths": 6,
  "city": "测试市",
  "description": "虚构测试动物",
  "isPublished": true
}
```

照片接受 JPEG/PNG/WebP，检查 MIME、签名与大小，默认五 MiB、每动物十张，禁止 SVG。忽略文件名并生成对象键；返回照片 URL，不公开磁盘路径。下架后照片不能公开读取。完整解码、压缩与内容审查不在本版实现内。

## 领养申请

| 方法/路径 | 权限/说明 |
|---|---|
| POST /api/applications | 登录，申请他人已上架动物，同账号每动物一次 |
| GET /api/applications/mine | 本人申请分页 |
| GET /api/applications/{id} | 仅申请人或对应动物发布者 |
| GET /api/my/animals/{animalId}/applications | 仅该动物发布者，收到的申请及联系方式 |
| PUT /api/my/applications/{id}/decision | 仅该动物发布者，{ status: "Approved" 或 "Rejected", note } |

申请示例（虚构数据）：

```json
{
  "animalId": 1,
  "name": "测试申请人",
  "phone": "13800000000",
  "weChat": "test_wechat",
  "residence": "测试住所",
  "petExperience": "测试养宠经验",
  "reason": "测试领养理由"
}
```

姓名、手机号、居住地、养宠经验和理由必填；手机号使用大陆十一位格式，首期不做短信验证。微信号选填，可省略或为 null，最多 64 字符。不收集身份证号。申请人和对应动物发布者可看完整申请，其他用户及公开动物 API 不包含联系方式。

申请人、创建时间和初始 Pending 状态由服务端确定。不能申请自己动物（400）；重复申请 409，未上架动物 404。发布者私下沟通后可记录结果，Pending 只能一次更新为 Approved/Rejected，已处理不可覆盖。通过结果不会自动下架动物，由发布者另行下架。

## 开发文档

Test/Development 提供 `GET /openapi/v1.json`，Production 不公开。API 客户端仍需真实 Cookie 和 CSRF，没有授权绕过接口。
