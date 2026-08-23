using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanstraction.Migrations
{
    /// <inheritdoc />
    public partial class RemovePresetDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LaborCost",
                table: "SubStagePresets");

            migrationBuilder.DropColumn(
                name: "Qty",
                table: "MaterialUsagesPreset");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LaborCost",
                table: "SubStagePresets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Qty",
                table: "MaterialUsagesPreset",
                type: "TEXT",
                nullable: true);
        }
    }
}
