using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PawHome.Api.Data;

/// <summary>EF 工具入口，生成迁移无需启动 HTTP 服务或读取邮件凭据。</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PawHomeDbContext>
{
    /// <summary>应用迁移时从环境变量读取连接；默认无密码占位连接仅用于生成脚本。</summary>
    public PawHomeDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__PawHome")
            ?? "Server=127.0.0.1;Database=pawhome;User ID=pawhome";
        return new(new DbContextOptionsBuilder<PawHomeDbContext>().UseMySQL(connection).Options);
    }
}
