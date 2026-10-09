using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PawHome.Api.Data;

namespace PawHome.Api.Auth;

/// <summary>集中生成邮箱确认令牌，注册和重新发送共用同一邮件流程。</summary>
public sealed class AccountNotifications(UserManager<AppUser> users, IAccountMailer mailer)
{
    /// <summary>发送 Identity 生成的邮箱确认令牌，不记录令牌内容。</summary>
    public async Task SendConfirmationAsync(AppUser user, CancellationToken cancellationToken) =>
        await mailer.SendAsync(new(user.Email!, "confirmation", user.Id, await users.GenerateEmailConfirmationTokenAsync(user)), cancellationToken);
}

/// <summary>统一把 Identity 校验错误转换为 MVC 模型错误，避免不同创建入口重复实现。</summary>
public static class IdentityValidation
{
    /// <summary>将框架错误加入已有模型状态，随后可返回标准 ValidationProblem。</summary>
    public static void AddIdentityErrors(this ModelStateDictionary state, IdentityResult result)
    {
        foreach (var error in result.Errors) state.AddModelError(error.Code, error.Description);
    }
}
