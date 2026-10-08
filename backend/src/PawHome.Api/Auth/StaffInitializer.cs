using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PawHome.Api.Data;
namespace PawHome.Api.Auth;
/// <summary>终端工作人员初始化服务，通过事务和唯一标记保证 Owner 只创建一次。</summary>
public sealed class StaffInitializer(PawHomeDbContext db, UserManager<AppUser> users, RoleManager<IdentityRole> roles)
{
    private const string OwnerMarker = "OwnerBootstrap";
    /// <summary>创建工作人员；Owner 的唯一标记与账号角色在同一事务提交。</summary>
    public async Task<(IdentityResult Result, AppUser? User)> CreateAsync(string role, string name, string email, string password)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (role == Roles.Owner)
        {
            var marker = new SetupMarker { Id = OwnerMarker };
            db.SetupMarkers.Add(marker);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                db.Entry(marker).State = EntityState.Detached;
                // 只有已存在同名标记才是重复初始化，连接等其他故障仍向上传递。
                if (await db.SetupMarkers.AnyAsync(x => x.Id == OwnerMarker))
                    return (Refused("Owner 初始化已经执行。"), null);
                throw;
            }
            if ((await users.GetUsersInRoleAsync(Roles.Owner)).Count != 0)
                return (Refused("Owner 已存在。"), null);
        }
        else if ((await users.GetUsersInRoleAsync(Roles.Owner)).Count == 0)
            return (Refused("请先初始化 Owner。"), null);
        var user = new AppUser { UserName = name, Email = email };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded) return (result, null);
        if (!await roles.RoleExistsAsync(role))
        {
            result = await roles.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded) return (result, null);
        }
        result = await users.AddToRoleAsync(user, role);
        if (!result.Succeeded) return (result, null);
        await transaction.CommitAsync();
        return (IdentityResult.Success, user);
    }
    /// <summary>将初始化拒绝转换为统一的 Identity 错误供终端输出。</summary>
    private static IdentityResult Refused(string description)
        => IdentityResult.Failed(new IdentityError { Code = "InitializationRefused", Description = description });
}
