using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PawHome.Api.Configuration;
using PawHome.Api.Data;

namespace PawHome.Api.Auth;

/// <summary>复用 Identity 的密码哈希、邮箱令牌和 Cookie 登录流程。</summary>
[ApiController, Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(UserManager<AppUser> users, SignInManager<AppUser> signIn,
    IAccountMailer mailer, AccountNotifications notifications, IOptions<SecurityOptions> security) : ControllerBase
{
    private const string RecoveryMessage = "如果账号符合条件，验证邮件已发送。";

    /// <summary>生成与浏览器 Cookie 绑定的防伪令牌，登录后应重新获取。</summary>
    [HttpGet("csrf")]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    /// <summary>注册统一账号，同一账号可发布动物及提交领养申请。</summary>
    [HttpPost("register"), EnableRateLimiting("mail")]
    public async Task<IActionResult> Register(RegisterInput input, CancellationToken cancellationToken)
    {
        var user = new AppUser { UserName = input.UserName, Email = input.Email };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded) return IdentityFailure(result);
        await notifications.SendConfirmationAsync(user, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, View(user));
    }

    /// <summary>验证有效账号与密码，固定 Cookie 到期时间，不要求邮箱验证后才能登录。</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginInput input)
    {
        var user = await users.FindByNameAsync(input.UserName);
        if (user is null || !user.IsActive) return Unauthorized();
        var result = await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized();
        await signIn.SignInAsync(user, new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(security.Value.SessionMinutes)
        });
        return Ok(View(user));
    }

    /// <summary>撤销当前浏览器会话。</summary>
    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    /// <summary>返回当前账号资料，适用于前端恢复登录状态。</summary>
    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me() => Ok(View((await users.GetUserAsync(User))!));

    /// <summary>验证邮箱所有权，Identity 负责令牌用途与有效期。</summary>
    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailInput input)
    {
        var user = await users.FindByIdAsync(input.UserId);
        if (user is null || !user.IsActive) return BadRequest(new { message = "验证请求无效。" });
        var result = await users.ConfirmEmailAsync(user, input.Token);
        return result.Succeeded ? NoContent() : BadRequest(new { message = "验证请求无效。" });
    }

    /// <summary>统一响应避免通过恢复流程枚举邮箱；仅向未验证有效账号发送。</summary>
    [HttpPost("resend-confirmation"), EnableRateLimiting("mail")]
    public async Task<IActionResult> ResendConfirmation(EmailInput input, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(input.Email);
        if (user is { IsActive: true, EmailConfirmed: false }) await notifications.SendConfirmationAsync(user, cancellationToken);
        return Accepted(new { message = RecoveryMessage });
    }

    /// <summary>仅已验证邮箱允许密码恢复，未知邮箱返回同样的响应。</summary>
    [HttpPost("forgot-password"), EnableRateLimiting("mail")]
    public async Task<IActionResult> ForgotPassword(EmailInput input, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(input.Email);
        if (user is { IsActive: true, EmailConfirmed: true })
            // 只向已验证的存储地址发送，避免数据库排序规则把输入变体匹配到其他域名。
            await mailer.SendAsync(new(user.Email!, "reset", user.Id, await users.GeneratePasswordResetTokenAsync(user)), cancellationToken);
        return Accepted(new { message = RecoveryMessage });
    }

    /// <summary>重置密码会更新安全戳，使旧令牌与所有既有 Cookie 立即失效。</summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordInput input)
    {
        var user = await users.FindByEmailAsync(input.Email);
        if (user is not { IsActive: true, EmailConfirmed: true }) return BadRequest(new { message = "重置请求无效。" });
        var result = await users.ResetPasswordAsync(user, input.Token, input.NewPassword);
        return result.Succeeded ? NoContent() : BadRequest(new { message = "重置请求无效或密码不符合规则。" });
    }

    /// <summary>集中投影账号资料，明确隔离 Identity 内部字段。</summary>
    private static UserView View(AppUser user) => new(user.Id, user.UserName!, user.Email!, user.EmailConfirmed);
    /// <summary>Identity 校验错误使用统一的模型验证响应。</summary>
    private IActionResult IdentityFailure(IdentityResult result)
    {
        ModelState.AddIdentityErrors(result);
        return ValidationProblem(ModelState);
    }
}
