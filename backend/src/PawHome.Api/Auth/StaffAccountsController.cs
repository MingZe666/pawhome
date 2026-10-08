using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PawHome.Api.Data;
using Microsoft.EntityFrameworkCore;
using PawHome.Api.Animals;

namespace PawHome.Api.Auth;

/// <summary>负责人仅管理志愿者账号，不提供自助升权或 Owner/Manager 编辑入口。</summary>
[ApiController, Route("api/staff/volunteers"), Authorize(Policy = Roles.ManageApplications)]
public sealed class StaffAccountsController(UserManager<AppUser> users, RoleManager<IdentityRole> roles,
    PawHomeDbContext database, AccountNotifications notifications) : ControllerBase
{
    /// <summary>分页列出志愿者账号，不返回领养申请资料和 Identity 内部字段。</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageQuery page, CancellationToken cancellationToken) => Ok(await database.Users.AsNoTracking()
        .Where(user => database.UserRoles.Any(assignment => assignment.UserId == user.Id &&
            database.Roles.Any(role => role.Id == assignment.RoleId && role.Name == Roles.Volunteer)))
        .OrderBy(user => user.UserName).Skip(page.Offset).Take(page.PageSize)
        .Select(user => new VolunteerView(user.Id, user.UserName!, user.Email!, user.EmailConfirmed, user.IsActive))
        .ToListAsync(cancellationToken));

    /// <summary>创建志愿者，只接受账号资料，不接受客户端角色字段。</summary>
    [HttpPost]
    public async Task<IActionResult> Create(RegisterInput input, CancellationToken cancellationToken)
    {
        var user = new AppUser { UserName = input.UserName, Email = input.Email };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            ModelState.AddIdentityErrors(result);
            return ValidationProblem(ModelState);
        }
        if (!await roles.RoleExistsAsync(Roles.Volunteer))
        {
            result = await roles.CreateAsync(new(Roles.Volunteer));
            if (!result.Succeeded) throw new InvalidOperationException("志愿者角色创建失败。");
        }
        result = await users.AddToRoleAsync(user, Roles.Volunteer);
        if (!result.Succeeded) throw new InvalidOperationException("志愿者角色授权失败。");
        await notifications.SendConfirmationAsync(user, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new UserView(user.Id, user.UserName!, user.Email!, user.EmailConfirmed, [Roles.Volunteer]));
    }

    /// <summary>停用纯志愿者账号并更新安全戳，下一次请求立即拒绝旧会话。</summary>
    [HttpPut("{id}/disable")]
    public async Task<IActionResult> Disable(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var assigned = await users.GetRolesAsync(user);
        // 禁止停用自己或拥有负责人角色的账号，即使同时具有志愿者角色。
        if (id == users.GetUserId(User) || !assigned.Contains(Roles.Volunteer) || assigned.Contains(Roles.Owner) || assigned.Contains(Roles.Manager))
            return Forbid();
        user.IsActive = false;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) throw new InvalidOperationException("账号停用失败。");
        result = await users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded) throw new InvalidOperationException("账号安全戳更新失败。");
        return NoContent();
    }
}

/// <summary>志愿者管理资料只包含账号基本信息和停用状态。</summary>
public sealed record VolunteerView(string Id, string UserName, string Email, bool EmailConfirmed, bool IsActive);
