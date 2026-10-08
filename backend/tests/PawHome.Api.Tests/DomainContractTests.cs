using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PawHome.Api.Tests;

/// <summary>验证公开档案与后台业务端点的认证边界。</summary>
public sealed class DomainContractTests
{
    /// <summary>匿名访问后台列表和提交申请必须返回未认证。</summary>
    [Theory]
    [InlineData("/api/staff/animals")]
    [InlineData("/api/staff/applications")]
    public async Task StaffListsRequireAuthentication(string path)
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }
}
