using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanstraction.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"WITH duplicates AS (
    SELECT Id
    FROM (
        SELECT Id,
               ROW_NUMBER() OVER (PARTITION BY SubStagePresetId, MaterialId ORDER BY Id) AS rn
        FROM MaterialUsagesPreset
    )
    WHERE rn > 1
)
DELETE FROM MaterialUsagesPreset
WHERE Id IN (SELECT Id FROM duplicates);");

            migrationBuilder.Sql(@"WITH duplicates AS (
    SELECT Id,
           ROW_NUMBER() OVER (PARTITION BY lower(Name) ORDER BY Id) AS rn
    FROM Materials
)
UPDATE Materials
SET Name = Name || ' (' || Id || ')'
WHERE Id IN (SELECT Id FROM duplicates WHERE rn > 1);");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialUsagesPreset_SubStagePresetId_MaterialId",
                table: "MaterialUsagesPreset",
                columns: new[] { "SubStagePresetId", "MaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_Name",
                table: "Materials",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MaterialUsagesPreset_SubStagePresetId_MaterialId",
                table: "MaterialUsagesPreset");

            migrationBuilder.DropIndex(
                name: "IX_Materials_Name",
                table: "Materials");
        }
    }
}
