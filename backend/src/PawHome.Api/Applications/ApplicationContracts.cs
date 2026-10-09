using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using PawHome.Api.Data;
namespace PawHome.Api.Applications;
/// <summary>领养申请输入，手机号必填、微信号选填；不接受申请人或状态覆盖。</summary>
public sealed record ApplicationInput(
    [Range(typeof(long), ApplicationInput.MinIdText, ApplicationInput.MaxIdText)] long AnimalId,
    [Required, StringLength(FieldLimits.Name)] string Name,
    [Required, StringLength(FieldLimits.Phone), RegularExpression(@"^1[3-9][0-9]{9}$")] string Phone,
    [Required, StringLength(FieldLimits.Address)] string Residence,
    [Required, StringLength(FieldLimits.Description)] string PetExperience,
    [Required, StringLength(FieldLimits.Description)] string Reason,
    [StringLength(FieldLimits.WeChat)] string? WeChat = null)
{
    public const string MinIdText = "1"; // 动物 ID 从一开始。
    public const string MaxIdText = "9223372036854775807"; // long 最大值，字符串避免 double 精度损失。
    // 手机号规则：大陆十一位号码，首位 1、第二位 3 至 9，后九位为十进制数字。
}
/// <summary>申请隐私响应，仅在申请人或对应动物发布者授权后返回。</summary>
public sealed record ApplicationView(long Id, long AnimalId, string Name, string Phone, string? WeChat, string Residence,
    string PetExperience, string Reason, ApplicationStatus Status, DateTimeOffset CreatedAt, string? DecisionNote)
{
    /// <summary>复用安全投影，不返回账号导航关系或 Identity 内部字段。</summary>
    public static readonly Expression<Func<AdoptionApplication, ApplicationView>> Projection = x =>
        new(x.Id, x.AnimalId, x.Name, x.Phone, x.WeChat, x.Residence, x.PetExperience, x.Reason,
            x.Status, x.CreatedAt, x.DecisionNote);
}
/// <summary>动物发布者记录私下沟通后的通过或拒绝结果。</summary>
public sealed record DecisionInput(ApplicationStatus Status, [StringLength(FieldLimits.Description)] string? Note);
