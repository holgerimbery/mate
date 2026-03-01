using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mate.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTestSuiteTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "TestSuites",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tags",
                table: "TestSuites");
        }
    }
}
