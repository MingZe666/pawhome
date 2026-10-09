using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using PawHome.Api.Animals;
using PawHome.Api.Data;
namespace PawHome.Api.Applications;
/// <summary>领养申请由申请人和对应动物发布者访问，取消全站申请管理权限。</summary>
[ApiController, Authorize]
public sealed class ApplicationsController(PawHomeDbContext db) : ControllerBase
{
    private const int DuplicateKeyError = 1062; // MySQL 唯一索引冲突错误码。
    /// <summary>登录账号向其他人的已上架动物提交一次申请，申请人由 Cookie 决定。</summary>
    [HttpPost("/api/applications")]
    public async Task<IActionResult> Submit(ApplicationInput input, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var animal = await db.Animals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.AnimalId && x.IsPublished, ct);
        if (animal is null) return NotFound();
        if (animal.PublisherId == userId) return BadRequest(new { message = "不能申请领养自己发布的动物。" });
        if (await db.Applications.AnyAsync(x => x.AnimalId == input.AnimalId && x.ApplicantId == userId, ct))
            return Conflict(new { message = "已经为该动物提交过申请。" });
        var application = new AdoptionApplication
        {
            AnimalId = input.AnimalId, ApplicantId = userId, Name = input.Name, Phone = input.Phone,
            WeChat = input.WeChat, Residence = input.Residence, PetExperience = input.PetExperience,
            Reason = input.Reason, CreatedAt = DateTimeOffset.UtcNow
        };
        db.Applications.Add(application);
        try { await db.SaveChangesAsync(ct); }
        // 唯一约束覆盖并发重复提交，其他故障交给统一异常处理。
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: DuplicateKeyError })
        { return Conflict(new { message = "已经为该动物提交过申请。" }); }
        return Created("/api/applications/" + application.Id, new { application.Id });
    }

    /// <summary>任何账号都可查询本人申请，即使同时是动物发布者。</summary>
    [HttpGet("/api/applications/mine")]
    public async Task<ActionResult<List<ApplicationView>>> Mine([FromQuery] PageQuery page, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return await db.Applications.AsNoTracking().Where(x => x.ApplicantId == userId)
            .OrderByDescending(x => x.Id).Skip(page.Offset).Take(page.PageSize)
            .Select(ApplicationView.Projection).ToListAsync(ct);
    }

    /// <summary>仅申请人和该动物发布者可读详情，其他账号统一返回未找到。</summary>
    [HttpGet("/api/applications/{id:long}")]
    public async Task<ActionResult<ApplicationView>> Detail(long id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var application = await db.Applications.AsNoTracking()
            .Where(x => x.Id == id && (x.ApplicantId == userId || x.Animal.PublisherId == userId))
            .Select(ApplicationView.Projection).SingleOrDefaultAsync(ct);
        return application is null ? NotFound() : application;
    }

    /// <summary>发布者只能查看自己动物收到的申请和联系方式。</summary>
    [HttpGet("/api/my/animals/{animalId:long}/applications")]
    public async Task<ActionResult<List<ApplicationView>>> Received(long animalId, [FromQuery] PageQuery page, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await db.Animals.AnyAsync(x => x.Id == animalId && x.PublisherId == userId, ct)) return NotFound();
        return await db.Applications.AsNoTracking().Where(x => x.AnimalId == animalId && x.Animal.PublisherId == userId)
            .OrderByDescending(x => x.Id).Skip(page.Offset).Take(page.PageSize)
            .Select(ApplicationView.Projection).ToListAsync(ct);
    }

    /// <summary>由对应动物发布者记录私下沟通结果，原子更新避免覆盖已处理状态。</summary>
    [HttpPut("/api/my/applications/{id:long}/decision")]
    public async Task<IActionResult> Decide(long id, DecisionInput input, CancellationToken ct)
    {
        if (input.Status is not (ApplicationStatus.Approved or ApplicationStatus.Rejected))
            return BadRequest(new { message = "结果必须为 Approved 或 Rejected。" });
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        // 通过 EXISTS 检查归属，避免 MySQL 提供程序把导航关联更新翻译为不支持的 UPDATE FROM。
        var authorized = db.Applications.Where(x => x.Id == id &&
            db.Animals.Any(animal => animal.Id == x.AnimalId && animal.PublisherId == userId));
        var updated = await authorized.Where(x => x.Status == ApplicationStatus.Pending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, input.Status)
                .SetProperty(x => x.DecisionNote, input.Note), ct);
        if (updated > 0) return NoContent(); // 更新一行代表首次记录结果成功。
        return await authorized.AnyAsync(ct) ? Conflict(new { message = "申请已处理，不能覆盖结果。" }) : NotFound();
    }
}
