using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanstraction.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingTypeSubStageReportFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludeInReport",
                table: "BuildingTypeSubStageLabors",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(@"UPDATE BuildingTypeSubStageLabors
SET IncludeInReport = 1
WHERE SubStagePresetId IN (
    SELECT s.Id
    FROM SubStagePresets s
    JOIN (
        SELECT StagePresetId, MAX(OrderIndex) AS MaxOrder
        FROM SubStagePresets
        GROUP BY StagePresetId
    ) last ON last.StagePresetId = s.StagePresetId
          AND last.MaxOrder = s.OrderIndex
);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludeInReport",
                table: "BuildingTypeSubStageLabors");
        }
    }
}
