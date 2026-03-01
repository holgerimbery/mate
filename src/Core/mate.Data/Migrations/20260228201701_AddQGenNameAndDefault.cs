using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mate.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQGenNameAndDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "GlobalQuestionGenerationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "GlobalQuestionGenerationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "GlobalQuestionGenerationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "GlobalQuestionGenerationSettings");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "GlobalQuestionGenerationSettings");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "GlobalQuestionGenerationSettings");
        }
    }
}
