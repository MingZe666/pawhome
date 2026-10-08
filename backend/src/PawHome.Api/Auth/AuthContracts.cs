using System.ComponentModel.DataAnnotations;
using PawHome.Api.Data;

namespace PawHome.Api.Auth;

/// <summary>注册与后台创建志愿者共用账号输入，密码规则交给 Identity。</summary>
public sealed record RegisterInput(
    [Required, StringLength(FieldLimits.Name)] string UserName,
    [Required] string Password,
    [Required, EmailAddress] string Email);
/// <summary>用户名与密码登录，不接收角色或令牌。</summary>
public sealed record LoginInput([Required] string UserName, [Required] string Password);
/// <summary>邮件请求统一使用邮箱地址。</summary>
public sealed record EmailInput([Required, EmailAddress] string Email);
/// <summary>确认邮件仅使用服务端生成的账号标识和令牌。</summary>
public sealed record ConfirmEmailInput([Required] string UserId, [Required] string Token);
/// <summary>密码重置通过已验证邮箱匹配账号。</summary>
public sealed record ResetPasswordInput([Required, EmailAddress] string Email,
    [Required] string Token, [Required] string NewPassword);
/// <summary>账号响应不包含密码哈希、安全戳或验证令牌。</summary>
public sealed record UserView(string Id, string UserName, string Email, bool EmailConfirmed, IList<string> Roles);
