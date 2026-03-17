using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mate.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantRoleAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantRoleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantRoleAssignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantRoleAssignments_IsActive",
                table: "TenantRoleAssignments",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_TenantRoleAssignments_TenantId_IsActive",
                table: "TenantRoleAssignments",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantRoleAssignments_TenantId_UserId",
                table: "TenantRoleAssignments",
                columns: new[] { "TenantId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantRoleAssignments");
        }
    }
}
