using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawHome.Api.Data;

namespace PawHome.Api.Tests;

/// <summary>验证邮箱作为密码恢复标识在数据库层也保持唯一。</summary>
public sealed class DatabaseContractTests
{
    /// <summary>即使绕过 Identity 的前置查询，并发写入也不能产生重复规范化邮箱。</summary>
    [Fact]
    public async Task DatabaseRejectsDuplicateRecoveryEmail()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        await app.SeedUserAsync("original");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PawHomeDbContext>();
        db.Users.Add(new AppUser
        {
            UserName = "other", NormalizedUserName = "OTHER",
            Email = "ORIGINAL@example.test", NormalizedEmail = "ORIGINAL@EXAMPLE.TEST"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
