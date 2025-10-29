using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanstraction.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingTypeSubStageLaborTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BuildingTypeSubStageLabors",
                columns: table => new
                {
                    BuildingTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubStagePresetId = table.Column<int>(type: "INTEGER", nullable: false),
                LaborCost = table.Column<decimal>(type: "TEXT", nullable: false)
            },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingTypeSubStageLabors", x => new { x.BuildingTypeId, x.SubStagePresetId });
                    table.ForeignKey(
                        name: "FK_BuildingTypeSubStageLabors_BuildingTypes_BuildingTypeId",
                        column: x => x.BuildingTypeId,
                        principalTable: "BuildingTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildingTypeSubStageLabors_SubStagePresets_SubStagePresetId",
                        column: x => x.SubStagePresetId,
                        principalTable: "SubStagePresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingTypeSubStageLabors_SubStagePresetId",
                table: "BuildingTypeSubStageLabors",
                column: "SubStagePresetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildingTypeSubStageLabors");
        }
    }
}
