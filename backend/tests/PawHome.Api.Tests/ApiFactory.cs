using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PawHome.Api.Data;
using MySql.Data.MySqlClient;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace PawHome.Api.Tests;

/// <summary>每个测试使用独立关系数据库与私有密钥、邮件和照片目录。</summary>
public sealed class ApiFactory(IReadOnlyDictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
{
    public const string Password = "TestPassword!42"; // 仅隔离测试账号使用的公开测试密码。
    public string PrivateRoot { get; } = Path.Combine(Path.GetTempPath(), "pawhome-tests", Guid.NewGuid().ToString("N"));
    private readonly string? mysqlConnection = CreateMySqlConnection();
    public TestClock Clock { get; } = new();

    /// <summary>显式启用真实 MySQL 时为每个工厂生成随机数据库名，避免改动既有数据库。</summary>
    private static string? CreateMySqlConnection()
    {
        var configured = Environment.GetEnvironmentVariable("PAWHOME_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(configured)) return null;
        return new MySqlConnectionStringBuilder(configured) { Database = "pawhome_" + Guid.NewGuid().ToString("N") }.ConnectionString;
    }

    /// <summary>替换数据库提供程序，真实业务和 Identity 仍使用相同 EF 模型。</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
            ["Security:KeyPath"] = Path.Combine(PrivateRoot, "keys"),
            ["Security:AuthPermitLimit"] = "1000", // 集成测试避免独立业务断言触发限流。
            ["Security:MailPermitLimit"] = "1000", // 限流测试可另行覆盖。
            ["Mail:Provider"] = "File",
            ["Mail:OutboxPath"] = Path.Combine(PrivateRoot, "outbox"),
            ["Photos:RootPath"] = Path.Combine(PrivateRoot, "photos")
            });
            if (overrides is not null) config.AddInMemoryCollection(overrides);
        });
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = Clock);
            services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = Clock);
            services.RemoveAll<DbContextOptions<PawHomeDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PawHomeDbContext>>();
            Directory.CreateDirectory(PrivateRoot);
            services.AddDbContext<PawHomeDbContext>(options =>
            {
                if (mysqlConnection is not null) options.UseMySQL(mysqlConnection);
                else options.UseSqlite($"Data Source={Path.Combine(PrivateRoot, "test.db")};Pooling=False");
            });
        });
    }

    /// <summary>创建 HTTPS 浏览器客户端，自动保存响应中的 Cookie。</summary>
    public HttpClient CreateCookieClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        using var scope = Services.CreateScope();
        InitializeDatabase(scope.ServiceProvider.GetRequiredService<PawHomeDbContext>());
        return client;
    }

    /// <summary>使用 Identity 真正创建测试账号与角色，而非绕过认证的模拟头。</summary>
    public async Task<AppUser> SeedUserAsync(string name, string? role = null, bool confirmed = false)
    {
        using var scope = Services.CreateScope();
        InitializeDatabase(scope.ServiceProvider.GetRequiredService<PawHomeDbContext>());
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = name, Email = $"{name}@example.test", EmailConfirmed = confirmed };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        if (role is not null)
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new(role))).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }
        return user;
    }

    /// <summary>使用真实登录端点取得 Cookie。</summary>
    public async Task LoginAsync(HttpClient client, string name, string password = Password)
    {
        var response = await PostAsync(client, "/api/auth/login", new { userName = name, password });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>为每次写入重新获取令牌，避免登录前后身份绑定变化。</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        if (body is HttpContent content) request.Content = content;
        else if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    /// <summary>发送带防伪令牌的 JSON POST。</summary>
    public Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body) => SendAsync(client, HttpMethod.Post, path, body);
    /// <summary>发送带防伪令牌的 JSON PUT。</summary>
    public Task<HttpResponseMessage> PutAsync(HttpClient client, string path, object body) => SendAsync(client, HttpMethod.Put, path, body);

    /// <summary>服务停止后删除本测试产生的私有文件。</summary>
    public override async ValueTask DisposeAsync()
    {
        if (mysqlConnection is not null)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PawHomeDbContext>().Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
        if (Directory.Exists(PrivateRoot)) Directory.Delete(PrivateRoot, recursive: true);
    }

    /// <summary>SQLite 验证关系约束，真实 MySQL 验证与部署相同的迁移。</summary>
    private void InitializeDatabase(PawHomeDbContext database)
    {
        if (mysqlConnection is not null) database.Database.Migrate();
        else database.Database.EnsureCreated();
    }
}

/// <summary>可推进的测试时钟，用于验证到期行为而无需等待两小时。</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset utcNow = DateTimeOffset.UtcNow;
    /// <summary>提供模拟服务器当前时间。</summary>
    public override DateTimeOffset GetUtcNow() => utcNow;
    /// <summary>按场景向前推进时间。</summary>
    public void Advance(TimeSpan duration) => utcNow += duration;
}
