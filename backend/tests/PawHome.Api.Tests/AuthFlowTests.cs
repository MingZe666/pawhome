using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PawHome.Api.Auth;
using PawHome.Api.Data;

namespace PawHome.Api.Tests;

/// <summary>使用真实 Identity、Cookie 和关系数据库验证账号安全行为。</summary>
public sealed class AuthFlowTests
{
    private const int SessionHours = 2; // 测试环境固定会话为两小时。

    /// <summary>恢复邮件只能发往数据库记录的邮箱，不能信任提交地址的大小写或字符变体。</summary>
    [Fact]
    public async Task RecoveryAlwaysUsesStoredDestination()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        await app.SeedUserAsync("destination", confirmed: true);
        await app.PostAsync(client, "/api/auth/forgot-password", new { email = "DESTINATION@EXAMPLE.TEST" });
        Assert.Equal("destination@example.test", (await ReadMailAsync(app, "reset")).To);
        using var scope = app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<PawHomeDbContext>();
        if (database.Database.ProviderName == "MySql.EntityFrameworkCore")
        {
            // MySQL 默认排序规则忽略重音；不同国际化域名也可能命中同一规范化邮箱。
            await app.PostAsync(client, "/api/auth/forgot-password", new { email = "destination@éxample.test" });
            var deliveries = Directory.EnumerateFiles(Path.Combine(app.PrivateRoot, "outbox"), "*.json")
                .Select(file => JsonSerializer.Deserialize<AccountMail>(File.ReadAllText(file))!).ToList();
            const int ExpectedDeliveries = 2; // 上述两个请求各产生一封邮件。
            Assert.Equal(ExpectedDeliveries, deliveries.Count);
            Assert.All(deliveries, delivery => Assert.Equal("destination@example.test", delivery.To));
        }
    }

    /// <summary>注册账号不包含角色且忽略客户端传入的角色，并允许邮箱验证前登录。</summary>
    [Fact]
    public async Task RegistrationCannotGrantRoleAndLoginUsesSecureCookie()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        var response = await app.PostAsync(client, "/api/auth/register", new
        {
            userName = "registrant", email = "registrant@example.test", password = ApiFactory.Password, roles = new[] { "Owner" }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var registration = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(registration.TryGetProperty("roles", out _));
        Assert.False(registration.GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.PostAsync(client, "/api/auth/login", new { userName = "registrant", password = "WrongPassword!42" })).StatusCode);
        response = await app.PostAsync(client, "/api/auth/login", new { userName = "registrant", password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-PawHome.Session="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsync(client, "/api/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    /// <summary>固定会话在活跃请求后仍按首次登录时间到期。</summary>
    [Fact]
    public async Task SessionExpiresAfterFixedLifetimeWithoutRenewal()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        await app.SeedUserAsync("session");
        await app.LoginAsync(client, "session");
        app.Clock.Advance(TimeSpan.FromHours(SessionHours / 2)); // 在会话中点发出活跃请求。
        var middle = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, middle.StatusCode);
        Assert.False(middle.Headers.Contains("Set-Cookie"));
        app.Clock.Advance(TimeSpan.FromHours(SessionHours / 2) + TimeSpan.FromMinutes(1)); // 超过固定到期时间。
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    /// <summary>未知账号与未验证邮箱的找回响应相同，并且不会生成密码重置邮件。</summary>
    [Fact]
    public async Task RecoveryDoesNotEnumerateAccountsOrUseUnverifiedEmail()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        await app.SeedUserAsync("unverified");
        var known = await app.PostAsync(client, "/api/auth/forgot-password", new { email = "unverified@example.test" });
        var unknown = await app.PostAsync(client, "/api/auth/forgot-password", new { email = "unknown@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.False(Directory.Exists(Path.Combine(app.PrivateRoot, "outbox")));
    }

    /// <summary>邮箱确认后可找回密码，重置令牌只能使用一次且旧 Cookie 立即失效。</summary>
    [Fact]
    public async Task ConfirmationAndResetRevokeOldSessionAndRejectReplay()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        await app.SeedUserAsync("recovery");
        await app.LoginAsync(client, "recovery");
        Assert.Equal(HttpStatusCode.Accepted, (await app.PostAsync(client, "/api/auth/resend-confirmation", new { email = "recovery@example.test" })).StatusCode);
        var confirmation = await ReadMailAsync(app, "confirmation");
        Assert.Equal(HttpStatusCode.BadRequest, (await app.PostAsync(client, "/api/auth/confirm-email", new { confirmation.UserId, token = "invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsync(client, "/api/auth/confirm-email", new { confirmation.UserId, confirmation.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await app.PostAsync(client, "/api/auth/forgot-password", new { email = "recovery@example.test" })).StatusCode);
        var reset = await ReadMailAsync(app, "reset");
        var resetBody = new { email = "recovery@example.test", reset.Token, newPassword = "ChangedPassword!43" };
        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsync(client, "/api/auth/reset-password", resetBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.PostAsync(client, "/api/auth/reset-password", resetBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.PostAsync(client, "/api/auth/login", new { userName = "recovery", password = ApiFactory.Password })).StatusCode);
        await app.LoginAsync(client, "recovery", "ChangedPassword!43");
    }

    /// <summary>保留账号停用后的安全戳保护，但不提供管理他人账号的 HTTP 入口。</summary>
    [Fact]
    public async Task DisablingAccountInvalidatesExistingSession()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        var user = await app.SeedUserAsync("disabled");
        await app.LoginAsync(client, "disabled");
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var stored = (await users.FindByIdAsync(user.Id))!;
            stored.IsActive = false;
            Assert.True((await users.UpdateAsync(stored)).Succeeded);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.PostAsync(client, "/api/auth/login",
            new { userName = "disabled", password = ApiFactory.Password })).StatusCode);
    }
    /// <summary>认证和邮件频率限制按环境配置，超限请求不继续发送邮件。</summary>
    [Fact]
    public async Task RateLimitsRejectExcessAuthenticationAndMailRequests()
    {
        await using var app = new ApiFactory(new Dictionary<string, string?> { ["Security:MailPermitLimit"] = "1" }); // 每窗口仅允许一次邮件请求。
        using var client = app.CreateCookieClient();
        Assert.Equal(HttpStatusCode.Accepted, (await app.PostAsync(client, "/api/auth/forgot-password", new { email = "absent@example.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await app.PostAsync(client, "/api/auth/forgot-password", new { email = "absent@example.test" })).StatusCode);
        await using var authApp = new ApiFactory(new Dictionary<string, string?> { ["Security:AuthPermitLimit"] = "1" }); // 每窗口仅允许一次认证请求。
        using var authClient = authApp.CreateCookieClient();
        Assert.Equal(HttpStatusCode.OK, (await authClient.GetAsync("/api/auth/csrf")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await authClient.GetAsync("/api/auth/csrf")).StatusCode);
    }

    /// <summary>从配置读取的令牌有效期必须作用于邮箱验证和密码恢复。</summary>
    [Fact]
    public async Task ExpiredTokensCannotConfirmEmailOrResetPassword()
    {
        await using var app = new ApiFactory(new Dictionary<string, string?> { ["Security:TokenMinutes"] = "-1" }); // 已过期窗口用于立即验证拒绝行为。
        using var client = app.CreateCookieClient();
        var user = await app.SeedUserAsync("expired", confirmed: true);
        var unconfirmed = await app.SeedUserAsync("expiredconfirmation");
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var reset = await users.GeneratePasswordResetTokenAsync(user);
        var confirmation = await users.GenerateEmailConfirmationTokenAsync(unconfirmed);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.PostAsync(client, "/api/auth/confirm-email", new { userId = unconfirmed.Id, token = confirmation })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.PostAsync(client, "/api/auth/reset-password", new { email = user.Email, token = reset, newPassword = "ChangedPassword!43" })).StatusCode);
    }

    /// <summary>读取私有测试发件箱，不通过 HTTP 暴露令牌。</summary>
    private static async Task<AccountMail> ReadMailAsync(ApiFactory app, string kind)
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(app.PrivateRoot, "outbox"), "*.json"))
        {
            var mail = JsonSerializer.Deserialize<AccountMail>(await File.ReadAllTextAsync(file))!;
            if (mail.Kind == kind) return mail;
        }
        throw new Xunit.Sdk.XunitException($"未生成 {kind} 邮件。");
    }
}
