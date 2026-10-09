using System.Net;


namespace PawHome.Api.Tests;

/// <summary>验证公开档案与个人业务端点的认证边界。</summary>
public sealed class DomainContractTests
{
    /// <summary>匿名访问个人列表和提交申请必须返回未认证。</summary>
    [Theory]
    [InlineData("/api/my/animals")]
    [InlineData("/api/my/animals/1/applications")]
    public async Task PersonalListsRequireAuthentication(string path)
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }
}
