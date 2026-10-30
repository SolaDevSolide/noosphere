using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Noosphere.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Collaboration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstanceSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    InstanceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrganisationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefaultLanguage = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstanceSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    ParentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    AttachedKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AttachedId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OwnerTeamId = table.Column<string>(type: "character varying(64)", nullable: true),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    Visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Links = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pages_Pages_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pages_Teams_OwnerTeamId",
                        column: x => x.OwnerTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Proposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Comment = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthorKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OnBehalfOf = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedById = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedByName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RefusalReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Revisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Operation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthorKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Revisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => new { x.UserId, x.TeamId });
                    table.ForeignKey(
                        name: "FK_TeamMembers_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProposalApprovals",
                columns: table => new
                {
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProposalApprovals", x => new { x.ProposalId, x.TeamId });
                    table.ForeignKey(
                        name: "FK_ProposalApprovals_Proposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "Proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProposalChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Operation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BaseVersion = table.Column<int>(type: "integer", nullable: true),
                    Before = table.Column<string>(type: "jsonb", nullable: true),
                    After = table.Column<string>(type: "jsonb", nullable: true),
                    Conflicts = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProposalChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProposalChanges_Proposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "Proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pages_AttachedKind_AttachedId",
                table: "Pages",
                columns: new[] { "AttachedKind", "AttachedId" });

            migrationBuilder.CreateIndex(
                name: "IX_Pages_Links",
                table: "Pages",
                column: "Links")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_OwnerTeamId",
                table: "Pages",
                column: "OwnerTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_ParentId",
                table: "Pages",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProposalChanges_Kind_TargetId",
                table: "ProposalChanges",
                columns: new[] { "Kind", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProposalChanges_ProposalId",
                table: "ProposalChanges",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_AuthorId",
                table: "Proposals",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_Status",
                table: "Proposals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Revisions_Kind_EntityId_Version",
                table: "Revisions",
                columns: new[] { "Kind", "EntityId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Revisions_ProposalId",
                table: "Revisions",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId",
                table: "TeamMembers",
                column: "TeamId");

            // Backfill: one revision per existing record, holding its current state (the snapshot has the shape of the
            // record's PUT body, enums in camelCase, empty fields left out).
            migrationBuilder.Sql("""
                insert into "Revisions" ("Kind", "EntityId", "Version", "Operation", "Snapshot", "AuthorName", "AuthorKind", "Summary", "CreatedAt")
                select k.kind, k.id, k.version, case when k.version = 1 then 'Create' else 'Update' end, jsonb_strip_nulls(k.snapshot),
                       'system', 'User', 'Recorded when the history was introduced', k.updated
                from (
                    select 'Domain' as kind, d."Id" as id, d."Version" as version, d."UpdatedAt" as updated,
                           jsonb_build_object('id', d."Id", 'name', d."Name", 'description', d."Description", 'order', d."Order") as snapshot
                    from "Domains" d
                    union all
                    select 'Team', t."Id", t."Version", t."UpdatedAt",
                           jsonb_build_object('id', t."Id", 'name', t."Name", 'description', t."Description", 'contact', t."Contact")
                    from "Teams" t
                    union all
                    select 'Application', a."Id", a."Version", a."UpdatedAt",
                           jsonb_build_object('id', a."Id", 'name', a."Name", 'domainId', a."DomainId", 'teamId', a."TeamId",
                               'kind', lower(a."Kind"), 'tech', a."Tech", 'description', a."Description",
                               'lifecycle', lower(a."Lifecycle"), 'visibility', lower(a."Visibility"))
                    from "Applications" a
                    union all
                    select 'DataObject', o."Id", o."Version", o."UpdatedAt",
                           jsonb_build_object('id', o."Id", 'name', o."Name", 'sensitivity', lower(o."Sensitivity"),
                               'sourceApplicationId', o."SourceApplicationId", 'description', o."Description")
                    from "DataObjects" o
                    union all
                    select 'Flow', f."Id", f."Version", f."UpdatedAt",
                           jsonb_build_object('id', f."Id", 'name', f."Name", 'fromId', f."FromId", 'toId', f."ToId", 'type', lower(f."Type"),
                               'schedule', f."Schedule", 'dataObjectIds', to_jsonb(f."DataObjectIds"), 'ownerTeamId', f."OwnerTeamId",
                               'status', lower(f."Status"), 'volume', f."Volume", 'description', f."Description",
                               'plannedChange', case when f."PlannedChangeSummary" is null then null
                                   else jsonb_strip_nulls(jsonb_build_object('summary', f."PlannedChangeSummary",
                                       'date', to_char(f."PlannedChangeDate", 'YYYY-MM-DD'))) end)
                    from "Flows" f
                ) k;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstanceSettings");

            migrationBuilder.DropTable(
                name: "Pages");

            migrationBuilder.DropTable(
                name: "ProposalApprovals");

            migrationBuilder.DropTable(
                name: "ProposalChanges");

            migrationBuilder.DropTable(
                name: "Revisions");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "Proposals");
        }
    }
}
