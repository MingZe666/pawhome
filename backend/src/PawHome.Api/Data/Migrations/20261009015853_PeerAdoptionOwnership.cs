using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace PawHome.Api.Data.Migrations
{
    /// <summary>切换为发布者归属授权，移除历史工作人员角色关系。</summary>
    public partial class PeerAdoptionOwnership : Migration
    {
        /// <summary>增加发布者和微信字段；归属未知档案安全下架并撤销旧会话。</summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "SetupMarkers");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.AddColumn<string>(
                name: "WeChat",
                table: "Applications",
                type: "varchar(64)",
                maxLength: 64, // 本迁移固定微信号长度六十四，保留历史结构而不依赖未来业务常量。
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublisherId",
                table: "Animals",
                type: "varchar(255)",
                nullable: true);

            // 历史档案无法证明发布归属，先下架且保留记录，禁止任意分配给现有账号。
            migrationBuilder.Sql("UPDATE Animals SET IsPublished = FALSE WHERE PublisherId IS NULL;");
            // 切换权限模型后所有旧登录及邮件令牌失效，要求重新登录和重新发送邮件。
            migrationBuilder.Sql("UPDATE AspNetUsers SET SecurityStamp = UUID();");

            migrationBuilder.CreateIndex(
                name: "IX_Animals_PublisherId_IsPublished",
                table: "Animals",
                columns: new[] { "PublisherId", "IsPublished" });

            migrationBuilder.AddForeignKey(
                name: "FK_Animals_AspNetUsers_PublisherId",
                table: "Animals",
                column: "PublisherId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <summary>仅恢复旧表结构；角色数据及发布归属须依靠部署前备份恢复。</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Animals_AspNetUsers_PublisherId",
                table: "Animals");

            migrationBuilder.DropIndex(
                name: "IX_Animals_PublisherId_IsPublished",
                table: "Animals");

            migrationBuilder.DropColumn(
                name: "WeChat",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "PublisherId",
                table: "Animals");

            // 以下长度还原 Identity 历史结构：ID 二百五十五、角色名二百五十六、初始化键三十二字符。
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "longtext", nullable: true),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SetupMarkers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetupMarkers", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClaimType = table.Column<string>(type: "longtext", nullable: true),
                    ClaimValue = table.Column<string>(type: "longtext", nullable: true),
                    RoleId = table.Column<string>(type: "varchar(255)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "varchar(255)", nullable: false),
                    RoleId = table.Column<string>(type: "varchar(255)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");
        }
    }
}
