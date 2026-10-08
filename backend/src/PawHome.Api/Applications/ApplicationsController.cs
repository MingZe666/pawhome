using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using PawHome.Api.Animals;
using PawHome.Api.Data;
namespace PawHome.Api.Applications;
/// <summary>资源级授权的领养申请接口。</summary>
[ApiController]
[Authorize]
public sealed class ApplicationsController(PawHomeDbContext db) : ControllerBase
{
    private const int DuplicateKeyError = 1062; // MySQL 唯一索引冲突错误码。

    /// <summary>登录用户只能为已发布动物提交一次申请。</summary>
    [HttpPost("/api/applications")]
    public async Task<IActionResult> Submit(ApplicationInput input, CancellationToken ct)
    {
        if (IsVolunteer) return Forbid();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await db.Animals.AnyAsync(x => x.Id == input.AnimalId && x.IsPublished, ct)) return NotFound();
        if (await db.Applications.AnyAsync(x => x.AnimalId == input.AnimalId && x.ApplicantId == userId, ct))
            return Conflict(new { message = "已经为该动物提交过申请。" });
        var application = new AdoptionApplication
        {
            AnimalId = input.AnimalId, ApplicantId = userId, Name = input.Name, Phone = input.Phone,
            Residence = input.Residence, PetExperience = input.PetExperience, Reason = input.Reason,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Applications.Add(application);
        try { await db.SaveChangesAsync(ct); }
        // 数据库唯一约束覆盖并发提交；其他数据库故障交给统一异常处理。
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: DuplicateKeyError })
        { return Conflict(new { message = "已经为该动物提交过申请。" }); }
        return Created("/api/applications/" + application.Id, new { application.Id });
    }

    /// <summary>志愿者职责不包含申请，禁止通过本人列表绕过该限制。</summary>
    private bool IsVolunteer => User.IsInRole(Roles.Volunteer);

    /// <summary>只读取当前账号的申请，不能通过参数切换申请人。</summary>
    [HttpGet("/api/applications/mine")]
    public async Task<ActionResult<List<ApplicationView>>> Mine([FromQuery] PageQuery page, CancellationToken ct)
    {
        if (IsVolunteer) return Forbid();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return await db.Applications.AsNoTracking().Where(x => x.ApplicantId == userId)
            .OrderByDescending(x => x.Id).Skip(page.Offset).Take(page.PageSize)
            .Select(ApplicationView.Projection).ToListAsync(ct);
    }

    /// <summary>他人申请统一返回未找到，避免泄露申请是否存在。</summary>
    [HttpGet("/api/applications/{id:long}")]
    public async Task<ActionResult<ApplicationView>> Detail(long id, CancellationToken ct)
    {
        if (IsVolunteer) return Forbid();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var application = await db.Applications.AsNoTracking()
            .Where(x => x.Id == id && x.ApplicantId == userId)
            .Select(ApplicationView.Projection).SingleOrDefaultAsync(ct);
        return application is null ? NotFound() : application;
    }

    /// <summary>负责人可分页查看所有申请及必要联系方式。</summary>
    [Authorize(Policy = Roles.ManageApplications)]
    [HttpGet("/api/staff/applications")]
    public async Task<ActionResult<List<ApplicationView>>> StaffList([FromQuery] PageQuery page, CancellationToken ct)
        => await db.Applications.AsNoTracking().OrderByDescending(x => x.Id).Skip(page.Offset).Take(page.PageSize)
            .Select(ApplicationView.Projection).ToListAsync(ct);

    /// <summary>原子更新待处理状态，避免多个负责人相互覆盖审核结果。</summary>
    [Authorize(Policy = Roles.ManageApplications)]
    [HttpPut("/api/staff/applications/{id:long}/decision")]
    public async Task<IActionResult> Decide(long id, DecisionInput input, CancellationToken ct)
    {
        if (input.Status is not (ApplicationStatus.Approved or ApplicationStatus.Rejected))
            return BadRequest(new { message = "审核结果必须为 Approved 或 Rejected。" });
        var updated = await db.Applications.Where(x => x.Id == id && x.Status == ApplicationStatus.Pending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, input.Status)
                .SetProperty(x => x.DecisionNote, input.Note), ct);
        if (updated > 0) return NoContent(); // 至少一行更新代表首次审核成功。
        return await db.Applications.AnyAsync(x => x.Id == id, ct)
            ? Conflict(new { message = "申请已处理，不能覆盖审核结果。" }) : NotFound();
    }
}
