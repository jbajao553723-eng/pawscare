using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawCare.Infrastructure.Migrations.Master
{
    /// <inheritdoc />
    public partial class AddCentralLoginRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoginRoutes",
                columns: table => new
                {
                    LoginRouteId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedUsername = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginRoutes", x => x.LoginRouteId);
                    table.ForeignKey(
                        name: "FK_LoginRoutes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "CompanyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoginRoutes_CompanyId",
                table: "LoginRoutes",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_LoginRoutes_NormalizedUsername",
                table: "LoginRoutes",
                column: "NormalizedUsername",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoginRoutes");
        }
    }
}
