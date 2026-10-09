using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PawHome.Api.Data;
namespace PawHome.Api.Animals;
/// <summary>公开动物查询与当前发布者的动物管理接口。</summary>
[ApiController]
public sealed class AnimalsController(PawHomeDbContext db, PublicationQuota quota) : ControllerBase
{
    /// <summary>访客只读取已上架动物，公开响应不包含私人联系方式。</summary>
    [HttpGet("/api/animals")]
    public async Task<ActionResult<List<AnimalView>>> List([FromQuery] PageQuery page, CancellationToken ct,
        [FromQuery, StringLength(FieldLimits.ShortText)] string? species = null)
    {
        var animals = db.Animals.AsNoTracking().Where(x => x.IsPublished);
        // 类型条件先于分页，前端切换猫狗时不会只筛选当前页。
        if (!string.IsNullOrEmpty(species)) animals = animals.Where(x => x.Species == species);
        return await animals.OrderByDescending(x => x.Id).Skip(page.Offset).Take(page.PageSize)
            .Select(AnimalView.Projection).ToListAsync(ct);
    }

    /// <summary>未上架档案不公开，统一返回未找到。</summary>
    [HttpGet("/api/animals/{id:long}")]
    public async Task<ActionResult<AnimalView>> Detail(long id, CancellationToken ct)
    {
        var animal = await db.Animals.AsNoTracking().Where(x => x.Id == id && x.IsPublished)
            .Select(AnimalView.Projection).SingleOrDefaultAsync(ct);
        return animal is null ? NotFound() : animal;
    }

    /// <summary>每个账号只查看自己发布的动物，包括草稿和已下架档案。</summary>
    [Authorize, HttpGet("/api/my/animals")]
    public async Task<ActionResult<List<AnimalView>>> Mine([FromQuery] PageQuery page, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return await db.Animals.AsNoTracking().Where(x => x.PublisherId == userId)
            .OrderByDescending(x => x.Id).Skip(page.Offset).Take(page.PageSize)
            .Select(AnimalView.Projection).ToListAsync(ct);
    }

    /// <summary>本人详情可读取未上架档案，别人不能通过 ID 获取草稿。</summary>
    [Authorize, HttpGet("/api/my/animals/{id:long}")]
    public async Task<ActionResult<AnimalView>> MyDetail(long id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var animal = await db.Animals.AsNoTracking().Where(x => x.Id == id && x.PublisherId == userId)
            .Select(AnimalView.Projection).SingleOrDefaultAsync(ct);
        return animal is null ? NotFound() : animal;
    }

    /// <summary>服务器固定归属为当前账号，创建已上架档案时原子检查三只名额。</summary>
    [Authorize, HttpPost("/api/my/animals")]
    public async Task<IActionResult> Create(AnimalInput input, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (input.IsPublished && !await quota.CanPublishAsync(userId, null, ct))
            return Conflict(new { message = "最多同时上架 3 个动物，请先下架其他动物或保存为草稿。" });
        var animal = new Animal { PublisherId = userId };
        input.Apply(animal);
        db.Animals.Add(animal);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Created("/api/my/animals/" + animal.Id, new { animal.Id });
    }

    /// <summary>只更新自己的档案，发布或重新上架时检查名额；编辑已上架动物不重复占名额。</summary>
    [Authorize, HttpPut("/api/my/animals/{id:long}")]
    public async Task<IActionResult> Update(long id, AnimalInput input, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // 先取得发布名额锁再读取档案，避免 MySQL 可重复读使用并发提交前的快照。
        var canPublish = !input.IsPublished || await quota.CanPublishAsync(userId, id, ct);
        var animal = await db.Animals.SingleOrDefaultAsync(x => x.Id == id && x.PublisherId == userId, ct);
        if (animal is null) return NotFound();
        if (!canPublish) return Conflict(new { message = "最多同时上架 3 个动物，请先下架其他动物。" });
        input.Apply(animal);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }
}
