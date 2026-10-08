using Microsoft.Extensions.Options;
namespace PawHome.Api.Storage;
/// <summary>本地照片适配器；迁移时整体复制目录并保留对象键即可。</summary>
public sealed class LocalPhotoStorage : IPhotoStorage
{
    private readonly string root;
    /// <summary>创建部署配置指定的私有目录，不放在静态网站目录下。</summary>
    public LocalPhotoStorage(IOptions<PhotoOptions> options, IWebHostEnvironment environment)
    {
        root = Path.GetFullPath(options.Value.RootPath, environment.ContentRootPath);
        Directory.CreateDirectory(root);
    }
    /// <summary>使用服务器生成的随机文件名，忽略上传的原文件名。</summary>
    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct)
    {
        var key = Guid.NewGuid().ToString("N") + extension;
        var path = Path.Combine(root, key);
        var file = new FileStream(path, FileMode.CreateNew);
        try
        {
            await using (file) await content.CopyToAsync(file, ct);
            return key;
        }
        catch
        {
            // 跨文件系统操作不能依赖数据库回滚，复制失败或取消时主动删除残留对象。
            File.Delete(path);
            throw;
        }
    }
    /// <summary>对象键只能是本目录的文件名，防止损坏元数据造成路径越界。</summary>
    private string Resolve(string key)
    {
        if (Path.GetFileName(key) != key) throw new InvalidOperationException("无效照片对象键。");
        return Path.Combine(root, key);
    }
    /// <summary>只读取受控目录的图片。</summary>
    public Stream OpenRead(string key) => File.OpenRead(Resolve(key));
    /// <summary>删除图片，不依赖外部服务。</summary>
    public void Delete(string key) => File.Delete(Resolve(key));
}
