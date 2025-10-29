using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanstraction.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaterialCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialCategories_Name",
                table: "MaterialCategories",
                column: "Name",
                unique: true);

            migrationBuilder.InsertData(
                table: "MaterialCategories",
                columns: new[] { "Id", "Name" },
                values: new object[] { 1, "Defaut" });

            migrationBuilder.AddColumn<int>(
                name: "MaterialCategoryId",
                table: "Materials",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_MaterialCategoryId",
                table: "Materials",
                column: "MaterialCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_MaterialCategories_MaterialCategoryId",
                table: "Materials",
                column: "MaterialCategoryId",
                principalTable: "MaterialCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materials_MaterialCategories_MaterialCategoryId",
                table: "Materials");

            migrationBuilder.DropIndex(
                name: "IX_Materials_MaterialCategoryId",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "MaterialCategoryId",
                table: "Materials");

            migrationBuilder.DropTable(
                name: "MaterialCategories");
        }
    }
}
