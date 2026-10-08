using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using PawHome.Api.Data;
namespace PawHome.Api.Applications;
/// <summary>用户提交申请字段，明确排除身份证号和状态等服务器字段。</summary>
public sealed record ApplicationInput(
    [Range(typeof(long), ApplicationInput.MinIdText, ApplicationInput.MaxIdText)] long AnimalId,
    [Required, StringLength(FieldLimits.Name)] string Name,
    [Required, StringLength(FieldLimits.Phone), RegularExpression(@"^1[3-9]\d{9}$")] string Phone,
    [Required, StringLength(FieldLimits.Address)] string Residence,
    [Required, StringLength(FieldLimits.Description)] string PetExperience,
    [Required, StringLength(FieldLimits.Description)] string Reason)
{
    public const string MinIdText = "1"; // 数据库生成的动物 ID 从一开始。
    public const string MaxIdText = "9223372036854775807"; // long 最大值，使用字符串避免 double 精度损失。
}
/// <summary>申请响应仅在本人和负责人授权接口返回。</summary>
public sealed record ApplicationView(long Id, long AnimalId, string Name, string Phone, string Residence,
    string PetExperience, string Reason, ApplicationStatus Status, DateTimeOffset CreatedAt, string? DecisionNote)
{
    /// <summary>复用申请投影，防止返回账号导航实体或密码字段。</summary>
    public static readonly Expression<Func<AdoptionApplication, ApplicationView>> Projection = x =>
        new(x.Id, x.AnimalId, x.Name, x.Phone, x.Residence, x.PetExperience, x.Reason,
            x.Status, x.CreatedAt, x.DecisionNote);
}
/// <summary>负责人只能将待处理申请设置为通过或拒绝。</summary>
public sealed record DecisionInput(ApplicationStatus Status,
    [StringLength(FieldLimits.Description)] string? Note);
