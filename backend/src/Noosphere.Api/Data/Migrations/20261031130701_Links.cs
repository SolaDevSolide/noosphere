using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Noosphere.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Links : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Milestone 5: links with gestion-mep (keys, pairing codes, exchange log, nonces) and the deployments they send.
            migrationBuilder.CreateTable(
                name: "Links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Product = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InstanceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PeerVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConfirmedHere = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PeerConfirmed = table.Column<bool>(type: "boolean", nullable: true),
                    PeerCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Capabilities = table.Column<List<string>>(type: "text[]", nullable: false),
                    OwnSigningKey = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    OwnSigningKid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnEncryptionKey = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    OwnEncryptionKid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnPreviousEncryptionKey = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    OwnPreviousEncryptionKid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OwnPreviousUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PeerSigningKey = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    PeerSigningKid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PeerEncryptionKey = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    PeerEncryptionKid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PeerPreviousSigningKey = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    PeerPreviousSigningKid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PeerPreviousUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PairedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PairedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KeysRenewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastExchangeAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CutBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Demo = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Deployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeploymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Postponed = table.Column<bool>(type: "boolean", nullable: false),
                    PlannedStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PlannedEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ApplicationIds = table.Column<List<string>>(type: "text[]", nullable: false),
                    Environments = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deployments_Links_LinkId",
                        column: x => x.LinkId,
                        principalTable: "Links",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LinkCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ProtectedCode = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Capabilities = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    BurntAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Bootstrap = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinkCodes_Links_LinkId",
                        column: x => x.LinkId,
                        principalTable: "Links",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LinkExchanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    Direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Operation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkExchanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinkExchanges_Links_LinkId",
                        column: x => x.LinkId,
                        principalTable: "Links",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LinkNonces",
                columns: table => new
                {
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nonce = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkNonces", x => new { x.LinkId, x.Nonce });
                    table.ForeignKey(
                        name: "FK_LinkNonces_Links_LinkId",
                        column: x => x.LinkId,
                        principalTable: "Links",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ApplicationIds",
                table: "Deployments",
                column: "ApplicationIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_EndedAt",
                table: "Deployments",
                column: "EndedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_LinkId_DeploymentId",
                table: "Deployments",
                columns: new[] { "LinkId", "DeploymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LinkCodes_CodeHash",
                table: "LinkCodes",
                column: "CodeHash");

            migrationBuilder.CreateIndex(
                name: "IX_LinkCodes_CodeId",
                table: "LinkCodes",
                column: "CodeId");

            migrationBuilder.CreateIndex(
                name: "IX_LinkCodes_LinkId",
                table: "LinkCodes",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_LinkExchanges_At",
                table: "LinkExchanges",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_LinkExchanges_LinkId_Id",
                table: "LinkExchanges",
                columns: new[] { "LinkId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_LinkNonces_SeenAt",
                table: "LinkNonces",
                column: "SeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_Links_PeerPreviousSigningKid",
                table: "Links",
                column: "PeerPreviousSigningKid");

            migrationBuilder.CreateIndex(
                name: "IX_Links_PeerSigningKid",
                table: "Links",
                column: "PeerSigningKid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Deployments");

            migrationBuilder.DropTable(
                name: "LinkCodes");

            migrationBuilder.DropTable(
                name: "LinkExchanges");

            migrationBuilder.DropTable(
                name: "LinkNonces");

            migrationBuilder.DropTable(
                name: "Links");
        }
    }
}
