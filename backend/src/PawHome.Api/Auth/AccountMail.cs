using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PawHome.Api.Configuration;

namespace PawHome.Api.Auth;

/// <summary>邮件内容共用契约，仅传给 SMTP 或测试私有发件箱。</summary>
public sealed record AccountMail(string To, string Kind, string UserId, string Token);
/// <summary>账号邮件发送接口，避免控制器依赖具体服务商。</summary>
public interface IAccountMailer
{
    /// <summary>发送确认或密码恢复邮件，不把令牌写入日志。</summary>
    Task SendAsync(AccountMail mail, CancellationToken cancellationToken);
}
/// <summary>真实邮件通过启用 TLS 的 SMTP 提交。</summary>
public sealed class SmtpAccountMailer(IOptions<MailOptions> options) : IAccountMailer
{
    /// <summary>发送包含账号标识和原始 Identity 令牌的邮件，客户端在表单中提交令牌。</summary>
    public async Task SendAsync(AccountMail mail, CancellationToken cancellationToken)
    {
        var config = options.Value;
        using var client = new SmtpClient(config.Host, config.Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(config.UserName, config.Password)
        };
        using var message = new MailMessage(config.From, mail.To)
        {
            Subject = mail.Kind == "confirmation" ? "PawHome 邮箱验证" : "PawHome 密码恢复",
            Body = $"账号标识：{mail.UserId}\n操作类型：{mail.Kind}\n验证码：{mail.Token}\n请在 PawHome 对应页面提交此验证码。若非本人操作，请忽略。"
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}
/// <summary>测试邮件写入非公开目录，不提供任何读取 HTTP 端点。</summary>
public sealed class FileAccountMailer(IOptions<MailOptions> options, IWebHostEnvironment environment) : IAccountMailer
{
    /// <summary>以独立随机文件保存邮件供部署测试读取，避免并发覆盖。</summary>
    public async Task SendAsync(AccountMail mail, CancellationToken cancellationToken)
    {
        var directory = Path.GetFullPath(options.Value.OutboxPath, environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(mail), cancellationToken);
    }
}
