using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanstraction.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingTypeMaterialUsages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Qty",
                table: "MaterialUsagesPreset",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.CreateTable(
                name: "BuildingTypeMaterialUsages",
                columns: table => new
                {
                    BuildingTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubStagePresetId = table.Column<int>(type: "INTEGER", nullable: false),
                    MaterialId = table.Column<int>(type: "INTEGER", nullable: false),
                    Qty = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingTypeMaterialUsages", x => new { x.BuildingTypeId, x.SubStagePresetId, x.MaterialId });
                    table.ForeignKey(
                        name: "FK_BuildingTypeMaterialUsages_BuildingTypes_BuildingTypeId",
                        column: x => x.BuildingTypeId,
                        principalTable: "BuildingTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildingTypeMaterialUsages_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildingTypeMaterialUsages_SubStagePresets_SubStagePresetId",
                        column: x => x.SubStagePresetId,
                        principalTable: "SubStagePresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingTypeMaterialUsages_SubStagePresetId",
                table: "BuildingTypeMaterialUsages",
                column: "SubStagePresetId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingTypeMaterialUsages_MaterialId",
                table: "BuildingTypeMaterialUsages",
                column: "MaterialId");

            migrationBuilder.Sql(
                """
                INSERT INTO "BuildingTypeMaterialUsages" ("BuildingTypeId", "SubStagePresetId", "MaterialId", "Qty")
                SELECT btsp."BuildingTypeId", mu."SubStagePresetId", mu."MaterialId", COALESCE(mu."Qty", 0)
                FROM "BuildingTypeStagePresets" AS btsp
                INNER JOIN "SubStagePresets" AS ssp ON ssp."StagePresetId" = btsp."StagePresetId"
                INNER JOIN "MaterialUsagesPreset" AS mu ON mu."SubStagePresetId" = ssp."Id"
                ;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildingTypeMaterialUsages");

            migrationBuilder.Sql("UPDATE \"MaterialUsagesPreset\" SET \"Qty\" = 0 WHERE \"Qty\" IS NULL;");

            migrationBuilder.AlterColumn<decimal>(
                name: "Qty",
                table: "MaterialUsagesPreset",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
