using Microsoft.AspNetCore.Identity;
namespace PawHome.Api.Data;
/// <summary>站点账号，复用 Identity 密码哈希和安全戳。</summary>
public sealed class AppUser : IdentityUser
{
    /// <summary>停用后禁止登录并撤销既有会话。</summary>
    public bool IsActive { get; set; } = true;
}
/// <summary>统一角色和授权策略名称。</summary>
public static class Roles
{
    public const string Owner = "Owner";
    public const string Manager = "Manager";
    public const string Volunteer = "Volunteer";
    public const string ManageAnimals = "ManageAnimals";
    public const string ManageApplications = "ManageApplications";
}
