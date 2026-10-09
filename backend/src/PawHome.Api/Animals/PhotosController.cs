using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PawHome.Api.Data;
using PawHome.Api.Storage;
namespace PawHome.Api.Animals;
/// <summary>受权限保护的照片上传，以及按动物发布状态读取照片。</summary>
[ApiController]
public sealed class PhotosController(PawHomeDbContext db, IPhotoStorage storage, IOptions<PhotoOptions> options) : ControllerBase
{
    private const int SignatureLength = 12; // WebP 文件头需要十二字节。
    private const int MaximumPhotos = 10; // 每只动物最多十张照片。
    private const int WebPMarkerOffset = 8; // WEBP 标记位于 RIFF 长度之后。

    /// <summary>上传 JPEG/PNG/WebP；同时检查大小、声明类型和二进制签名。</summary>
    [Authorize]
    [HttpPost("/api/my/animals/{id:long}/photos")]
    [RequestSizeLimit(PhotoOptions.RequestMaxBytes)]
    public async Task<IActionResult> Upload(long id, IFormFile file, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var animal = await db.Animals.SingleOrDefaultAsync(x => x.Id == id && x.PublisherId == userId, ct);
        if (animal is null) return NotFound();
        if (file.Length <= 0 || file.Length > options.Value.MaxBytes) // 空文件或超出配置上限不能上传。
            return BadRequest(new { message = "照片为空或超过大小限制。" });
        await using var source = file.OpenReadStream();
        var signature = new byte[SignatureLength];
        var read = await source.ReadAtLeastAsync(signature, SignatureLength, throwOnEndOfStream: false, ct);
        var extension = DetectExtension(signature.AsSpan(0, read), file.ContentType);
        if (extension is null) return BadRequest(new { message = "仅接受 JPEG、PNG 或 WebP 图片。" });
        source.Position = 0; // 保存完整内容，包含已读取的文件头。
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // 不改变字段值的 UPDATE 取得动物行写锁，串行化同一动物的上传计数和插入。
        // 此方式同时支持 MySQL 与 SQLite，避免依赖专有 FOR UPDATE 语法。
        await db.Animals.Where(x => x.Id == id && x.PublisherId == userId).ExecuteUpdateAsync(
            setters => setters.SetProperty(x => x.IsPublished, x => x.IsPublished), ct);
        if (await db.AnimalPhotos.CountAsync(x => x.AnimalId == id, ct) >= MaximumPhotos)
            return Conflict(new { message = "该动物照片数量已达上限。" });
        var key = await storage.SaveAsync(source, extension, ct);
        var photo = new AnimalPhoto { AnimalId = id, StorageKey = key, ContentType = file.ContentType };
        db.AnimalPhotos.Add(photo);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            // 元数据写入或提交失败（含取消）时删除新对象，并保留原异常。
            storage.Delete(key);
            throw;
        }
        return Created("/api/animals/" + id + "/photos/" + photo.Id, new PhotoView(photo.Id,
            "/api/animals/" + id + "/photos/" + photo.Id));
    }

    /// <summary>匹配图片签名与 MIME，拒绝可执行的 SVG 和伪装文本。</summary>
    private static string? DetectExtension(ReadOnlySpan<byte> header, string contentType)
    {
        // JPEG 前三个字节固定为 FF D8 FF；PNG 使用完整八字节签名。
        if (contentType == "image/jpeg" && header.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) return ".jpg";
        if (contentType == "image/png" && header.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return ".png";
        if (contentType == "image/webp" && header.Length >= SignatureLength &&
            header.StartsWith("RIFF"u8) && header[WebPMarkerOffset..].StartsWith("WEBP"u8)) return ".webp";
        return null;
    }

    /// <summary>未发布动物照片只对发布者本人开放，不通过磁盘路径直接公开。</summary>
    [HttpGet("/api/animals/{id:long}/photos/{photoId:long}")]
    public async Task<IActionResult> Read(long id, long photoId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var photo = await db.AnimalPhotos.AsNoTracking()
            .Where(x => x.Id == photoId && x.AnimalId == id && (x.Animal.IsPublished || (userId != null && x.Animal.PublisherId == userId)))
            .SingleOrDefaultAsync(ct);
        if (photo is null) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "no-store"; // 下架和权限变化后不能继续使用缓存图片。
        return File(storage.OpenRead(photo.StorageKey), photo.ContentType);
    }

    /// <summary>删除照片元数据及对应存储对象。</summary>
    [Authorize]
    [HttpDelete("/api/my/animals/{id:long}/photos/{photoId:long}")]
    public async Task<IActionResult> Delete(long id, long photoId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var photo = await db.AnimalPhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.AnimalId == id && x.Animal.PublisherId == userId, ct);
        if (photo is null) return NotFound();
        db.AnimalPhotos.Remove(photo);
        await db.SaveChangesAsync(ct);
        storage.Delete(photo.StorageKey);
        return NoContent();
    }
}
