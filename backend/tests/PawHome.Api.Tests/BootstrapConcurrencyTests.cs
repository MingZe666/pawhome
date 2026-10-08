using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PawHome.Api.Auth;
using PawHome.Api.Data;
namespace PawHome.Api.Tests;
/// <summary>验证并发执行初始化不会产生多个主负责人。</summary>
public sealed class BootstrapConcurrencyTests
{
    /// <summary>两个独立作用域模拟两次终端初始化，数据库只允许一个成功。</summary>
    [Fact]
    public async Task ConcurrentOwnerInitializationCreatesExactlyOneOwner()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        using var first = app.Services.CreateScope();
        using var second = app.Services.CreateScope();
        var firstInitializer = first.ServiceProvider.GetRequiredService<StaffInitializer>();
        var secondInitializer = second.ServiceProvider.GetRequiredService<StaffInitializer>();
        var attempts = await Task.WhenAll(
            firstInitializer.CreateAsync(Roles.Owner, "firstowner", "first@example.test", ApiFactory.Password),
            secondInitializer.CreateAsync(Roles.Owner, "secondowner", "second@example.test", ApiFactory.Password));
        Assert.Single(attempts, x => x.Result.Succeeded);
        using var verification = app.Services.CreateScope();
        var users = verification.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Single(await users.GetUsersInRoleAsync(Roles.Owner));
    }
}
