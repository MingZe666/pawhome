// 迁移生成说明：结构数字是历史数据库契约，不引用可变业务常量。
// varchar 长度来自 Identity 默认上限或 FieldLimits；tinyint(1) 为布尔类型，标识符上限 64 来自 MySQL。
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawHome.Api.Data.Migrations
{
    /// <summary>固定历史数据库迁移，记录当时的模型约束。</summary>
    /// <summary>EF 自动生成的历史模型；业务修改请新增迁移。</summary>
    public partial class UniqueRecoveryEmail : Migration
    {
        /// <summary>应用本次数据库结构变更。</summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <summary>回退本次结构变更；部署前应先备份业务数据。</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");
        }
    }
}
