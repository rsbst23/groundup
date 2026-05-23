using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroundUp.Auth.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthFlowStatesAndDropTenantCustomDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomDomain",
                table: "AuthTenants");

            migrationBuilder.CreateTable(
                name: "AuthFlowStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvitationId = table.Column<Guid>(type: "uuid", nullable: true),
                    JoinLinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    Realm = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReturnUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Nonce = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedByIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByUserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TerminatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthFlowStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthFlowStates_Status_ExpiresAt",
                table: "AuthFlowStates",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthFlowStates_TenantId",
                table: "AuthFlowStates",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthFlowStates");

            migrationBuilder.AddColumn<string>(
                name: "CustomDomain",
                table: "AuthTenants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
