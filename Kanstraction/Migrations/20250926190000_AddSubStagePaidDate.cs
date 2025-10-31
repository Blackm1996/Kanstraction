using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanstraction.Migrations
{
    /// <inheritdoc />
    public partial class AddSubStagePaidDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime?>
            (
                name: "PaidDate",
                table: "SubStages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                @"UPDATE SubStages
SET PaidDate = COALESCE(EndDate, DATE('now'))
WHERE Status = 3 AND PaidDate IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaidDate",
                table: "SubStages");
        }
    }
}
