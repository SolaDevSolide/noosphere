using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Noosphere.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Agents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Milestone 4: AI agents (OAuth clients, requests, tokens, connections, activity), their settings, agent proposals.
            migrationBuilder.AddColumn<Guid>(
                name: "AgentConnectionId",
                table: "Proposals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ClientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientUri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scopes = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PausedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PendingApproval = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Demo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentConnections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Ceiling = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QuotaPerHour = table.Column<int>(type: "integer", nullable: false),
                    WhoConnects = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DynamicRegistration = table.Column<bool>(type: "boolean", nullable: false),
                    AnonymousLlms = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OAuthClients",
                columns: table => new
                {
                    ClientId = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ClientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientUri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RedirectUris = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthClients", x => x.ClientId);
                });

            migrationBuilder.CreateTable(
                name: "OAuthRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ClientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientUri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RedirectUri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    State = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CodeChallenge = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Scopes = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CodeUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeniedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentActivity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tool = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RecordKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RecordId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentActivity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentActivity_AgentConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "AgentConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Scopes = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTokens_AgentConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "AgentConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_AgentConnectionId",
                table: "Proposals",
                column: "AgentConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentActivity_At",
                table: "AgentActivity",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AgentActivity_ConnectionId_At",
                table: "AgentActivity",
                columns: new[] { "ConnectionId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentConnections_ClientId",
                table: "AgentConnections",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentConnections_UserId",
                table: "AgentConnections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTokens_ConnectionId",
                table: "AgentTokens",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTokens_Hash",
                table: "AgentTokens",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OAuthRequests_CodeHash",
                table: "OAuthRequests",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OAuthRequests_ExpiresAt",
                table: "OAuthRequests",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentActivity");

            migrationBuilder.DropTable(
                name: "AgentSettings");

            migrationBuilder.DropTable(
                name: "AgentTokens");

            migrationBuilder.DropTable(
                name: "OAuthClients");

            migrationBuilder.DropTable(
                name: "OAuthRequests");

            migrationBuilder.DropTable(
                name: "AgentConnections");

            migrationBuilder.DropIndex(
                name: "IX_Proposals_AgentConnectionId",
                table: "Proposals");

            migrationBuilder.DropColumn(
                name: "AgentConnectionId",
                table: "Proposals");
        }
    }
}
