using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noosphere.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Imports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Formats = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CatalogueStamp = table.Column<long>(type: "bigint", nullable: false),
                    Creates = table.Column<int>(type: "integer", nullable: false),
                    Updates = table.Column<int>(type: "integer", nullable: false),
                    Unchanged = table.Column<int>(type: "integer", nullable: false),
                    Warnings = table.Column<int>(type: "integer", nullable: false),
                    Errors = table.Column<int>(type: "integer", nullable: false),
                    Preview = table.Column<string>(type: "jsonb", nullable: false),
                    Operations = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportPlans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportPlans_CreatedAt",
                table: "ImportPlans",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ImportPlans_UserId",
                table: "ImportPlans",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportPlans");
        }
    }
}
