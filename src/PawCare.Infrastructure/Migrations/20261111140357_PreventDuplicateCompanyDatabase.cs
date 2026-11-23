using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawCare.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PreventDuplicateCompanyDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CompanyDatabases_ServerName_DatabaseName",
                table: "CompanyDatabases",
                columns: new[] { "ServerName", "DatabaseName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompanyDatabases_ServerName_DatabaseName",
                table: "CompanyDatabases");
        }
    }
}
