using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noosphere.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShareEmails",
                table: "Links",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShareEmails",
                table: "Links");
        }
    }
}
