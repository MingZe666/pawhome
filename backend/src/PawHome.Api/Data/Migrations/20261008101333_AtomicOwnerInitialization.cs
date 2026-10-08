// 迁移生成说明：数字是历史数据库契约；字段长度来自 Identity 或 FieldLimits，tinyint(1) 表示布尔值，64 是 MySQL 标识符上限。
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawHome.Api.Data.Migrations
{
    /// <summary>记录不可变的历史数据库结构变更。</summary>
    public partial class AtomicOwnerInitialization : Migration
    {
        /// <summary>应用本次数据库结构变更。</summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <summary>回退本次结构变更，执行前应先备份。</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SetupMarkers");
        }
    }
}
