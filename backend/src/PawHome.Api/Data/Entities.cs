namespace PawHome.Api.Data;
/// <summary>持久化的一次性初始化标记，主键防止多个终端同时创建 Owner。</summary>
public sealed class SetupMarker
{
    public string Id { get; set; } = "";
}
/// <summary>动物公开档案，不保存申请联系方式。</summary>
public sealed class Animal
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Species { get; set; } = "";
    public string Sex { get; set; } = "";
    public int AgeMonths { get; set; }
    public string City { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsPublished { get; set; }
    public List<AnimalPhoto> Photos { get; set; } = [];
}
/// <summary>照片元数据，保存可迁移的存储对象键。</summary>
public sealed class AnimalPhoto
{
    public long Id { get; set; }
    public long AnimalId { get; set; }
    public Animal Animal { get; set; } = null!;
    public string StorageKey { get; set; } = "";
    public string ContentType { get; set; } = "";
}
/// <summary>申请状态：待处理、通过、拒绝。</summary>
public enum ApplicationStatus { Pending, Approved, Rejected }
/// <summary>领养申请及隐私信息，不收集身份证号。</summary>
public sealed class AdoptionApplication
{
    public long Id { get; set; }
    public long AnimalId { get; set; }
    public Animal Animal { get; set; } = null!;
    public string ApplicantId { get; set; } = "";
    public AppUser Applicant { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Residence { get; set; } = "";
    public string PetExperience { get; set; } = "";
    public string Reason { get; set; } = "";
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public string? DecisionNote { get; set; }
}
/// <summary>DTO 与数据库共用的字段长度和分页限制。</summary>
public static class FieldLimits
{
    public const int Name = 100; // 姓名与动物名称字符数上限。
    public const int ShortText = 32; // 短文本字符数上限。
    public const int Address = 300; // 居住地字符数上限。
    public const int Description = 2000; // 描述、理由字符数上限。
    public const int Phone = 20; // 电话字符数上限。
    public const int StorageKey = 100; // 随机对象键字符数上限。
    public const int PageSize = 20; // 默认每页数量。
    public const int MaxPageSize = 100; // 最大每页数量。
}
