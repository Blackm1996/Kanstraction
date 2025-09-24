using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanstraction.Migrations
{
    public partial class BuildingTypeLaborCosts : Migration
    {
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

            migrationBuilder.Sql(@"
                INSERT INTO BuildingTypeSubStageLabors (BuildingTypeId, SubStagePresetId, LaborCost)
                SELECT btsp.BuildingTypeId, ssp.Id, ssp.LaborCost
                FROM BuildingTypeStagePresets btsp
                INNER JOIN SubStagePresets ssp ON ssp.StagePresetId = btsp.StagePresetId
            ");

            migrationBuilder.DropColumn(
                name: "LaborCost",
                table: "SubStagePresets");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LaborCost",
                table: "SubStagePresets",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(@"
                UPDATE SubStagePresets
                SET LaborCost = (
                    SELECT LaborCost
                    FROM BuildingTypeSubStageLabors btsl
                    WHERE btsl.SubStagePresetId = SubStagePresets.Id
                    LIMIT 1
                )
                WHERE EXISTS (
                    SELECT 1 FROM BuildingTypeSubStageLabors btsl
                    WHERE btsl.SubStagePresetId = SubStagePresets.Id
                );
            ");

            migrationBuilder.DropTable(
                name: "BuildingTypeSubStageLabors");
        }
    }
}
