using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Noosphere.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccessAndProtectedNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Milestone 3: access lists on applications and pages, protected notes on applications and flows, access log.
            migrationBuilder.AddColumn<List<string>>(
                name: "AccessTeamIds",
                table: "Pages",
                type: "text[]",
                nullable: false,
                // Existing records get an empty access list.
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "AccessUsernames",
                table: "Pages",
                type: "text[]",
                nullable: false,
                // Existing records get an empty access list.
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "ProtectedNotes",
                table: "Flows",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "AccessTeamIds",
                table: "Applications",
                type: "text[]",
                nullable: false,
                // Existing records get an empty access list.
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "AccessUsernames",
                table: "Applications",
                type: "text[]",
                nullable: false,
                // Existing records get an empty access list.
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "ProtectedNotes",
                table: "Applications",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccessLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EntityId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    What = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Admin = table.Column<bool>(type: "boolean", nullable: false),
                    Why = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    WhyTeamId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLog_At",
                table: "AccessLog",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AccessLog_Kind_EntityId",
                table: "AccessLog",
                columns: new[] { "Kind", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLog_Username",
                table: "AccessLog",
                column: "Username");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessLog");

            migrationBuilder.DropColumn(
                name: "AccessTeamIds",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "AccessUsernames",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "ProtectedNotes",
                table: "Flows");

            migrationBuilder.DropColumn(
                name: "AccessTeamIds",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "AccessUsernames",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "ProtectedNotes",
                table: "Applications");
        }
    }
}
