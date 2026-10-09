using System.ComponentModel.DataAnnotations;
namespace PawHome.Api.Storage;
/// <summary>照片存储配置，位置不绑定云厂商。</summary>
public sealed class PhotoOptions
{
    public const long DefaultMaxBytes = 5 * 1024 * 1024; // 默认单张照片上限五 MiB。
    public const long RequestMaxBytes = 6 * 1024 * 1024; // 留出 multipart 表单头空间。
    [Required] public string RootPath { get; set; } = "App_Data/photos";
    [Range(1, DefaultMaxBytes)] public long MaxBytes { get; set; } = DefaultMaxBytes; // 至少一字节，最多五 MiB。
}
/// <summary>存储接口，元数据只保留返回的对象键。</summary>
public interface IPhotoStorage
{
    /// <summary>保存已验证的图片流，返回唯一对象键。</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct);
    /// <summary>读取可信元数据中的对象键。</summary>
    Stream OpenRead(string key);
    /// <summary>删除照片对象；调用者负责元数据权限校验。</summary>
    void Delete(string key);
}
