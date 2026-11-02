using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noosphere.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FailingSince",
                table: "Webhooks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PausedAt",
                table: "Webhooks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedById",
                table: "ApiKeys",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailingSince",
                table: "Webhooks");

            migrationBuilder.DropColumn(
                name: "PausedAt",
                table: "Webhooks");

            migrationBuilder.DropColumn(
                name: "ReplacedById",
                table: "ApiKeys");
        }
    }
}
