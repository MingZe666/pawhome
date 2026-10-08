using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PawHome.Api.Tests;

/// <summary>认证协议必须提供防伪令牌并保护所有写入。</summary>
public sealed class AuthContractTests
{
    /// <summary>匿名浏览器可获取与其 Cookie 配套的防伪令牌。</summary>
    [Fact]
    public async Task CsrfTokenIsAvailable()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/csrf")).StatusCode);
    }

    /// <summary>登录也必须拒绝未提供防伪令牌的跨站请求。</summary>
    [Fact]
    public async Task LoginRequiresCsrf()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { userName = "test", password = "TestPassword!42" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>登录后写入必须使用新身份的防伪令牌，匿名阶段令牌不能退出已登录会话。</summary>
    [Fact]
    public async Task LoginChangesCsrfIdentityAndProtectsAuthenticatedWrites()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        await app.SeedUserAsync("csrfidentity");
        var anonymousToken = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        await app.LoginAsync(client, "csrfidentity");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        using var staleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        staleRequest.Headers.Add("X-CSRF-TOKEN", anonymousToken.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(staleRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsync(client, "/api/auth/logout", new { })).StatusCode);
    }
}
