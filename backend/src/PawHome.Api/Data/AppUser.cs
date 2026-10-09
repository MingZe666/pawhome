using Microsoft.AspNetCore.Identity;
namespace PawHome.Api.Data;
/// <summary>站点账号，复用 Identity 密码哈希和安全戳。</summary>
public sealed class AppUser : IdentityUser
{
    /// <summary>停用后禁止登录并撤销既有会话。</summary>
    public bool IsActive { get; set; } = true;
}
