using Kanstraction.OnSite.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanstraction.Migrations;

[DbContext(typeof(OnSiteDbContext))]
[Migration("20260730000000_AddBuildingCodeUniqueness")]
public sealed class AddBuildingCodeUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Buildings_ProjectId",
            table: "Buildings");

        migrationBuilder.CreateIndex(
            name: "IX_Buildings_ProjectId_Code",
            table: "Buildings",
            columns: new[] { "ProjectId", "Code" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Buildings_ProjectId_Code",
            table: "Buildings");

        migrationBuilder.CreateIndex(
            name: "IX_Buildings_ProjectId",
            table: "Buildings",
            column: "ProjectId");
    }
}
