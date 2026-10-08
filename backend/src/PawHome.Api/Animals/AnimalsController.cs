using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PawHome.Api.Data;
namespace PawHome.Api.Animals;
/// <summary>公开动物档案与管理员编辑接口。</summary>
[ApiController]
public sealed class AnimalsController(PawHomeDbContext db) : ControllerBase
{
    /// <summary>访客只读取已发布动物，不返回申请或账号信息。</summary>
    [HttpGet("/api/animals")]
    public async Task<ActionResult<List<AnimalView>>> List([FromQuery] PageQuery page, CancellationToken ct)
        => await db.Animals.AsNoTracking().Where(x => x.IsPublished).OrderByDescending(x => x.Id)
            .Skip(page.Offset).Take(page.PageSize).Select(AnimalView.Projection).ToListAsync(ct);

    /// <summary>公开详情对不存在和未发布档案统一返回未找到。</summary>
    [HttpGet("/api/animals/{id:long}")]
    public async Task<ActionResult<AnimalView>> Detail(long id, CancellationToken ct)
    {
        var animal = await db.Animals.AsNoTracking().Where(x => x.Id == id && x.IsPublished)
            .Select(AnimalView.Projection).SingleOrDefaultAsync(ct);
        return animal is null ? NotFound() : animal;
    }

    /// <summary>志愿者和负责人可查看全部动物，包含未发布档案。</summary>
    [Authorize(Policy = Roles.ManageAnimals)]
    [HttpGet("/api/staff/animals")]
    public async Task<ActionResult<List<AnimalView>>> StaffList([FromQuery] PageQuery page, CancellationToken ct)
        => await db.Animals.AsNoTracking().OrderByDescending(x => x.Id).Skip(page.Offset).Take(page.PageSize)
            .Select(AnimalView.Projection).ToListAsync(ct);

    /// <summary>管理员创建动物档案。</summary>
    [Authorize(Policy = Roles.ManageAnimals)]
    [HttpPost("/api/staff/animals")]
    public async Task<IActionResult> Create(AnimalInput input, CancellationToken ct)
    {
        var animal = new Animal();
        input.Apply(animal);
        db.Animals.Add(animal);
        await db.SaveChangesAsync(ct);
        return Created("/api/animals/" + animal.Id, new { animal.Id });
    }

    /// <summary>共用写入字段更新档案，通过 IsPublished 执行发布或下架。</summary>
    [Authorize(Policy = Roles.ManageAnimals)]
    [HttpPut("/api/staff/animals/{id:long}")]
    public async Task<IActionResult> Update(long id, AnimalInput input, CancellationToken ct)
    {
        var animal = await db.Animals.FindAsync([id], ct);
        if (animal is null) return NotFound();
        input.Apply(animal);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
