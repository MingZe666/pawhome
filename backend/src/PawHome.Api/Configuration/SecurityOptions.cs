namespace PawHome.Api.Configuration;

/// <summary>可按环境调整的会话、令牌和限流配置，时间单位为分钟。</summary>
public sealed class SecurityOptions
{
    public int SessionMinutes { get; set; } = 120; // 测试默认会话固定两小时。
    public int TokenMinutes { get; set; } = 60; // 邮箱确认与密码重置令牌默认一小时。
    public int AuthPermitLimit { get; set; } = 30; // 每个来源在窗口内允许的认证请求数。
    public int MailPermitLimit { get; set; } = 5; // 每个来源在窗口内允许的邮件请求数。
    public int RateWindowMinutes { get; set; } = 1; // 固定限流窗口一分钟。
    public string KeyPath { get; set; } = "App_Data/keys";
}

/// <summary>邮件适配配置，生产使用 SMTP，测试使用私有文件发件箱。</summary>
public sealed class MailOptions
{
    public string Provider { get; set; } = "Smtp";
    public string OutboxPath { get; set; } = "App_Data/outbox";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587; // SMTP 提交端口，使用 STARTTLS。
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
}
