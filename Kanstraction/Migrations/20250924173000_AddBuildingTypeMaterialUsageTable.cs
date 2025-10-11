using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanstraction.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingTypeMaterialUsageTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BuildingTypeMaterialUsages",
                columns: table => new
                {
                    BuildingTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    MaterialUsagePresetId = table.Column<int>(type: "INTEGER", nullable: false),
                    Qty = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingTypeMaterialUsages", x => new { x.BuildingTypeId, x.MaterialUsagePresetId });
                    table.ForeignKey(
                        name: "FK_BuildingTypeMaterialUsages_BuildingTypes_BuildingTypeId",
                        column: x => x.BuildingTypeId,
                        principalTable: "BuildingTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildingTypeMaterialUsages_MaterialUsagesPreset_MaterialUsagePresetId",
                        column: x => x.MaterialUsagePresetId,
                        principalTable: "MaterialUsagesPreset",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingTypeMaterialUsages_MaterialUsagePresetId",
                table: "BuildingTypeMaterialUsages",
                column: "MaterialUsagePresetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildingTypeMaterialUsages");
        }
    }
}
