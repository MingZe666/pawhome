using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using PawHome.Api.Data;
namespace PawHome.Api.Tests;
/// <summary>在真实 MySQL 模式验证历史档案不会在升级时泄露或被错误分配。</summary>
public sealed class OwnershipMigrationTests
{
    private const string PreviousMigration = "20261008101333_AtomicOwnerInitialization"; // 更改归属前的最后版本。
    /// <summary>保留账号和动物数据，旧无归属动物下架，并要求旧账号重新登录。</summary>
    [Fact]
    public async Task UpgradeUnpublishesUnownedAnimalsAndRevokesLegacySessions()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PawHomeDbContext>();
        // SQLite 只验证当前模型，MySQL 模式才执行包含厂商专用 DDL 的历史升级。
        if (db.Database.ProviderName != "MySql.EntityFrameworkCore") return;
        await db.Database.EnsureDeletedAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        const string LegacyStamp = "legacy-session"; // 虚构的历史会话安全戳。
        var user = new AppUser { UserName = "legacy", Email = "legacy@example.test", SecurityStamp = LegacyStamp };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        // 旧表还没有 PublisherId，只插入旧版本支持的字段。
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Animals (Name, Species, Sex, AgeMonths, City, Description, IsPublished)
            VALUES ('测试旧动物', '猫', '母', 6, '测试市', '历史虚构数据', TRUE);
            """); // 六个月的虚构动物，仅用于升级测试。
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        var animal = await db.Animals.SingleAsync();
        Assert.Null(animal.PublisherId);
        Assert.False(animal.IsPublished);
        Assert.Equal("测试旧动物", animal.Name);
        Assert.NotEqual(LegacyStamp, (await db.Users.SingleAsync()).SecurityStamp);
    }
}
