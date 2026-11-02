using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Noosphere.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Integrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApiKeyId",
                table: "LinkExchanges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WebhookId",
                table: "LinkExchanges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "LinkId",
                table: "Deployments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "ApiKeyId",
                table: "Deployments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Scopes = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AllowPrivateWebhookAddresses = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Webhooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ProtectedSecret = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Events = table.Column<List<string>>(type: "text[]", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastDeliveryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Webhooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotentRequests",
                columns: table => new
                {
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Response = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotentRequests", x => new { x.ApiKeyId, x.Key });
                    table.ForeignKey(
                        name: "FK_IdempotentRequests_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WebhookId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResponseStatus = table.Column<int>(type: "integer", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Test = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_Webhooks_WebhookId",
                        column: x => x.WebhookId,
                        principalTable: "Webhooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LinkExchanges_ApiKeyId_Id",
                table: "LinkExchanges",
                columns: new[] { "ApiKeyId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_LinkExchanges_WebhookId_Id",
                table: "LinkExchanges",
                columns: new[] { "WebhookId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ApiKeyId_DeploymentId",
                table: "Deployments",
                columns: new[] { "ApiKeyId", "DeploymentId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Deployments_Source",
                table: "Deployments",
                sql: "(\"LinkId\" IS NULL) <> (\"ApiKeyId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_Prefix",
                table: "ApiKeys",
                column: "Prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotentRequests_CreatedAt",
                table: "IdempotentRequests",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_CreatedAt",
                table: "WebhookDeliveries",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_State_NextAttemptAt",
                table: "WebhookDeliveries",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_WebhookId_Id",
                table: "WebhookDeliveries",
                columns: new[] { "WebhookId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_Deployments_ApiKeys_ApiKeyId",
                table: "Deployments",
                column: "ApiKeyId",
                principalTable: "ApiKeys",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LinkExchanges_ApiKeys_ApiKeyId",
                table: "LinkExchanges",
                column: "ApiKeyId",
                principalTable: "ApiKeys",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LinkExchanges_Webhooks_WebhookId",
                table: "LinkExchanges",
                column: "WebhookId",
                principalTable: "Webhooks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Deployments_ApiKeys_ApiKeyId",
                table: "Deployments");

            migrationBuilder.DropForeignKey(
                name: "FK_LinkExchanges_ApiKeys_ApiKeyId",
                table: "LinkExchanges");

            migrationBuilder.DropForeignKey(
                name: "FK_LinkExchanges_Webhooks_WebhookId",
                table: "LinkExchanges");

            migrationBuilder.DropTable(
                name: "IdempotentRequests");

            migrationBuilder.DropTable(
                name: "IntegrationSettings");

            migrationBuilder.DropTable(
                name: "WebhookDeliveries");

            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "Webhooks");

            migrationBuilder.DropIndex(
                name: "IX_LinkExchanges_ApiKeyId_Id",
                table: "LinkExchanges");

            migrationBuilder.DropIndex(
                name: "IX_LinkExchanges_WebhookId_Id",
                table: "LinkExchanges");

            migrationBuilder.DropIndex(
                name: "IX_Deployments_ApiKeyId_DeploymentId",
                table: "Deployments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Deployments_Source",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "ApiKeyId",
                table: "LinkExchanges");

            migrationBuilder.DropColumn(
                name: "WebhookId",
                table: "LinkExchanges");

            migrationBuilder.DropColumn(
                name: "ApiKeyId",
                table: "Deployments");

            migrationBuilder.AlterColumn<Guid>(
                name: "LinkId",
                table: "Deployments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
