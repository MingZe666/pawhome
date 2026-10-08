using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PawHome.Api.Data;

namespace PawHome.Api.Auth;

/// <summary>仅在服务器终端运行的迁移和管理员初始化命令。</summary>
public static class BootstrapCommands
{
    private const int Success = 0; // 标准进程成功退出码。
    private const int Refused = 1; // 初始化未执行或校验失败。

    /// <summary>迁移或显式创建工作人员；密码仅从隐藏终端输入，禁止参数、默认值和日志。</summary>
    public static async Task<int> RunAsync(string command, IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        if (command == "migrate")
        {
            await scope.ServiceProvider.GetRequiredService<PawHomeDbContext>().Database.MigrateAsync();
            Console.WriteLine("数据库迁移完成。");
            return Success;
        }
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var role = command == "bootstrap-owner" ? Roles.Owner : Roles.Manager;
        // 已存在 Owner 时必须停止，不修改原账号，也不创建第二个 Owner。
        if (role == Roles.Owner && (await users.GetUsersInRoleAsync(Roles.Owner)).Count != 0)
        {
            Console.Error.WriteLine("Owner 已存在，初始化已停止。");
            return Refused;
        }
        if (role == Roles.Manager && (await users.GetUsersInRoleAsync(Roles.Owner)).Count == 0)
        {
            Console.Error.WriteLine("请先初始化 Owner。");
            return Refused;
        }
        var name = configuration["user-name"];
        var email = configuration["email"];
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || Console.IsInputRedirected)
        {
            Console.Error.WriteLine("需要 --user-name、--email 和可交互终端输入密码。");
            return Refused;
        }
        var initialization = await scope.ServiceProvider.GetRequiredService<StaffInitializer>()
            .CreateAsync(role, name, email, ReadPassword());
        var result = initialization.Result;
        if (!result.Succeeded)
        {
            Console.Error.WriteLine(string.Join("；", result.Errors.Select(error => error.Description)));
            return Refused;
        }
        await scope.ServiceProvider.GetRequiredService<AccountNotifications>()
            .SendConfirmationAsync(initialization.User!, CancellationToken.None);
        Console.WriteLine($"{role} 账号创建完成，请验证邮箱以启用密码恢复。");
        return Success;
    }

    /// <summary>隐藏输入密码，不在终端回显任何字符。</summary>
    private static string ReadPassword()
    {
        Console.Write("密码（不回显）：");
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return password.ToString(); }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0) password.Length--;
            }
            else if (!char.IsControl(key.KeyChar)) password.Append(key.KeyChar);
        }
    }
}
