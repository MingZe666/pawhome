using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using PawHome.Api.Data;
namespace PawHome.Api.Animals;
/// <summary>动物写入字段，仅供有动物管理权限的账号使用。</summary>
public sealed record AnimalInput(
    [Required, StringLength(FieldLimits.Name)] string Name,
    [Required, StringLength(FieldLimits.ShortText)] string Species,
    [Required, StringLength(FieldLimits.ShortText)] string Sex,
    [Range(AnimalInput.MinAgeMonths, AnimalInput.MaxAgeMonths)] int AgeMonths,
    [Required, StringLength(FieldLimits.Address)] string City,
    [Required, StringLength(FieldLimits.Description)] string Description,
    bool IsPublished)
{
    public const int MinAgeMonths = 0; // 幼崽可不足一个月。
    public const int MaxAgeMonths = 600; // 允许最长五十年的档案年龄。
    /// <summary>共用创建与编辑赋值逻辑，禁止客户端写入照片对象键。</summary>
    public void Apply(Animal animal)
    {
        animal.Name = Name; animal.Species = Species; animal.Sex = Sex;
        animal.AgeMonths = AgeMonths; animal.City = City;
        animal.Description = Description; animal.IsPublished = IsPublished;
    }
}
/// <summary>安全照片链接，不暴露服务器磁盘路径。</summary>
public sealed record PhotoView(long Id, string Url);
/// <summary>动物公开响应，与申请模型独立以防泄露联系方式。</summary>
public sealed record AnimalView(long Id, string Name, string Species, string Sex, int AgeMonths,
    string City, string Description, bool IsPublished, List<PhotoView> Photos)
{
    /// <summary>统一公开和管理员列表投影，避免重复序列化实体导航关系。</summary>
    public static readonly Expression<Func<Animal, AnimalView>> Projection = animal => new(
        animal.Id, animal.Name, animal.Species, animal.Sex, animal.AgeMonths,
        animal.City, animal.Description, animal.IsPublished,
        animal.Photos.OrderBy(photo => photo.Id).Select(photo => new PhotoView(photo.Id,
            "/api/animals/" + photo.AnimalId + "/photos/" + photo.Id)).ToList());
}
/// <summary>分页参数，限制列表返回量。</summary>
public sealed class PageQuery
{
    public const int FirstPage = 1; // 页码从一开始。
    public const int MaxPage = 1000000; // 限制偏移计算范围。
    [Range(FirstPage, MaxPage)] public int Page { get; set; } = FirstPage;
    [Range(FirstPage, FieldLimits.MaxPageSize)] public int PageSize { get; set; } = FieldLimits.PageSize;
    /// <summary>将页码转换为数据库偏移。</summary>
    public int Offset => (Page - FirstPage) * PageSize;
}
