using Microsoft.EntityFrameworkCore;
using PawHome.Api.Data;
namespace PawHome.Api.Animals;
/// <summary>复用发布名额检查，通过账号行写锁串行化同一求助人的发布操作。</summary>
public sealed class PublicationQuota(PawHomeDbContext db)
{
    public const int MaximumPublishedAnimals = 3; // 每个账号最多同时上架三只动物，草稿不计入。
    /// <summary>在调用方的数据库事务内锁定账号并检查名额；编辑时排除当前动物。</summary>
    public async Task<bool> CanPublishAsync(string userId, long? excludingAnimalId, CancellationToken ct)
    {
        // 不改变启用状态，只取得账号行写锁，使两个并发发布不能同时消耗最后名额。
        await db.Users.Where(x => x.Id == userId).ExecuteUpdateAsync(
            setters => setters.SetProperty(x => x.IsActive, x => x.IsActive), ct);
        return await db.Animals.CountAsync(x => x.PublisherId == userId && x.IsPublished &&
            (excludingAnimalId == null || x.Id != excludingAnimalId), ct) < MaximumPublishedAnimals;
    }
}
