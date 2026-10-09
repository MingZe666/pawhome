using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace PawHome.Api.Data;
/// <summary>关系数据入口，只使用 Identity 账号模型；权限来自动物归属。</summary>
public sealed class PawHomeDbContext(DbContextOptions<PawHomeDbContext> options) : IdentityUserContext<AppUser>(options)
{
    public DbSet<Animal> Animals => Set<Animal>();
    public DbSet<AnimalPhoto> AnimalPhotos => Set<AnimalPhoto>();
    public DbSet<AdoptionApplication> Applications => Set<AdoptionApplication>();
    /// <summary>配置业务约束，迁移与服务共用模型。</summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // 邮箱用于恢复账号，数据库约束覆盖并发注册，避免出现多个同邮箱账号。
        builder.Entity<AppUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.Entity<Animal>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(FieldLimits.Name);
            e.Property(x => x.Species).HasMaxLength(FieldLimits.ShortText);
            e.Property(x => x.Sex).HasMaxLength(FieldLimits.ShortText);
            e.Property(x => x.City).HasMaxLength(FieldLimits.Address);
            e.Property(x => x.Description).HasMaxLength(FieldLimits.Description);
            e.HasIndex(x => x.IsPublished);
            e.HasIndex(x => new { x.PublisherId, x.IsPublished });
            // 归属不能随账号删除而转移，历史申请与发布者关系必须保留。
            e.HasOne(x => x.Publisher).WithMany().HasForeignKey(x => x.PublisherId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<AnimalPhoto>(e =>
        {
            e.Property(x => x.StorageKey).HasMaxLength(FieldLimits.StorageKey);
            e.Property(x => x.ContentType).HasMaxLength(FieldLimits.ShortText);
            e.HasOne(x => x.Animal).WithMany(x => x.Photos).HasForeignKey(x => x.AnimalId);
        });
        builder.Entity<AdoptionApplication>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(FieldLimits.Name);
            e.Property(x => x.Phone).HasMaxLength(FieldLimits.Phone);
            e.Property(x => x.WeChat).HasMaxLength(FieldLimits.WeChat);
            e.Property(x => x.Residence).HasMaxLength(FieldLimits.Address);
            e.Property(x => x.PetExperience).HasMaxLength(FieldLimits.Description);
            e.Property(x => x.Reason).HasMaxLength(FieldLimits.Description);
            e.Property(x => x.DecisionNote).HasMaxLength(FieldLimits.Description);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(FieldLimits.ShortText);
            // 历史申请不随动物或账号删除而级联丢失。
            e.HasOne(x => x.Animal).WithMany().HasForeignKey(x => x.AnimalId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Applicant).WithMany().HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Restrict);
            // 同一用户对同一动物只能申请一次，避免并发重复申请。
            e.HasIndex(x => new { x.ApplicantId, x.AnimalId }).IsUnique();
        });
    }
}
