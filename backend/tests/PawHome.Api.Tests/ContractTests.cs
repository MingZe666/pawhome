using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
namespace PawHome.Api.Tests;
/// <summary>认证端点的集成合同测试。</summary>
public sealed class ContractTests
{
    /// <summary>未登录访问本人申请应返回未认证。</summary>
    [Fact]
    public async Task AnonymousCannotReadApplications()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/applications/mine")).StatusCode);
    }
}
