using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mate.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSuiteDelayBetweenTests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DelayBetweenTestsMs",
                table: "TestSuites",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DelayBetweenTestsMs",
                table: "TestSuites");
        }
    }
}
