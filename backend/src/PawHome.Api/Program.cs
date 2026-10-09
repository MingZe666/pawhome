using PawHome.Api.Animals;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PawHome.Api.Auth;
using PawHome.Api.Configuration;
using PawHome.Api.Data;
using PawHome.Api.Storage;

// 数据库迁移命令复用服务配置，账号统一由用户自行注册。
var command = args.FirstOrDefault();
var isCommand = command == "migrate";
var builder = WebApplication.CreateBuilder(isCommand ? args.Skip(1).ToArray() : args);
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));
builder.Services.AddOptions<MailOptions>().Bind(builder.Configuration.GetSection("Mail"))
    .Validate(mail => mail.Provider is "File" or "Smtp", "Mail:Provider 必须为 File 或 Smtp。")
    .Validate(mail => !builder.Environment.IsProduction() ||
        (mail.Provider == "Smtp" && !string.IsNullOrWhiteSpace(mail.Host) && !string.IsNullOrWhiteSpace(mail.From)),
        "生产环境必须使用 SMTP 并配置 Mail:Host 和 Mail:From。")
    .ValidateOnStart();
builder.Services.AddScoped<AccountNotifications>();
builder.Services.AddScoped<PublicationQuota>();
builder.Services.AddOptions<PhotoOptions>().Bind(builder.Configuration.GetSection("Photos"))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<IPhotoStorage, LocalPhotoStorage>();
builder.Services.AddDbContext<PawHomeDbContext>(options => options.UseMySQL(
    builder.Configuration.GetConnectionString("PawHome") ?? ""));
builder.Services.AddDataProtection().SetApplicationName("PawHome");
// 在服务解析时读取环境配置，测试和部署可安全替换私有密钥目录。
builder.Services.AddOptions<KeyManagementOptions>().Configure<IOptions<SecurityOptions>, IWebHostEnvironment, ILoggerFactory>(
    (options, security, environment, logger) => options.XmlRepository = new FileSystemXmlRepository(
        new DirectoryInfo(Path.GetFullPath(security.Value.KeyPath, environment.ContentRootPath)), logger));
builder.Services.AddIdentityCore<AppUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = SecurityDefaults.MinimumPasswordLength;
    options.SignIn.RequireConfirmedEmail = false;
    options.Lockout.MaxFailedAccessAttempts = SecurityDefaults.LockoutAttempts;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(SecurityDefaults.LockoutMinutes);
}).AddEntityFrameworkStores<PawHomeDbContext>().AddSignInManager().AddDefaultTokenProviders();
// 注册 Identity Cookie 方案，使登录与退出复用框架会话机制。
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddOptions<DataProtectionTokenProviderOptions>().Configure<IOptions<SecurityOptions>>(
    (options, security) => options.TokenLifespan = TimeSpan.FromMinutes(security.Value.TokenMinutes));
// 每个请求验证安全戳，密码重置、停用和权限撤销不等待默认缓存窗口。
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "__Host-PawHome.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    options.Events.OnValidatePrincipal = async context =>
    {
        var manager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var user = await manager.GetUserAsync(context.Principal!);
        // 停用账号立即撤销会话，不再使用管理员角色授权。
        if (user is not { IsActive: true })
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        context.ShouldRenew = false; // 禁止安全戳验证刷新会话到期时间，保持固定时长。
    };
});
builder.Services.AddOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
    .Configure<IOptions<SecurityOptions>>((options, security) => options.ExpireTimeSpan = TimeSpan.FromMinutes(security.Value.SessionMinutes));
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-PawHome.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddAuthorization();
// 内置 MVC 防伪授权筛选器由 Views 服务注册，继续复用框架验证而不手写校验。
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // 来源 IP 分区，不把用户提交的账号或邮箱当作限流键。
    options.AddPolicy("auth", context =>
    {
        var security = context.RequestServices.GetRequiredService<IOptions<SecurityOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => SecurityDefaults.Window(security.AuthPermitLimit, security));
    });
    options.AddPolicy("mail", context =>
    {
        var security = context.RequestServices.GetRequiredService<IOptions<SecurityOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => SecurityDefaults.Window(security.MailPermitLimit, security));
    });
});
// 读取完整环境配置后选择适配器，文件发件箱只允许测试或开发环境。
builder.Services.AddSingleton<IAccountMailer>(services =>
{
    var mail = services.GetRequiredService<IOptions<MailOptions>>();
    var environment = services.GetRequiredService<IWebHostEnvironment>();
    return mail.Value.Provider == "File" ? new FileAccountMailer(mail, environment) : new SmtpAccountMailer(mail);
});
var app = builder.Build();
if (isCommand)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PawHomeDbContext>().Database.MigrateAsync();
    Console.WriteLine("数据库迁移完成。");
    return;
}
app.UseExceptionHandler();
// 账号和申请接口包含私人信息，禁止浏览器或代理缓存 API 响应。
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
if (!app.Environment.IsProduction()) app.MapOpenApi();
app.Run();

/// <summary>公开入口供集成测试启动隔离服务。</summary>
public partial class Program { }

/// <summary>安全策略中的固定业务默认值，集中说明数字含义。</summary>
internal static class SecurityDefaults
{
    public const int MinimumPasswordLength = 10; // 密码至少十个字符，另由 Identity 要求大小写、数字和符号。
    public const int LockoutAttempts = 5; // 连续五次错误密码后短暂锁定。
    public const int LockoutMinutes = 5; // 锁定五分钟减少暴力尝试。
    /// <summary>生成没有排队等待的固定窗口限流设置。</summary>
    public static FixedWindowRateLimiterOptions Window(int permitLimit, SecurityOptions security) => new()
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromMinutes(security.RateWindowMinutes),
        QueueLimit = 0 // 超限立即拒绝，避免积压认证和邮件请求。
    };
}
