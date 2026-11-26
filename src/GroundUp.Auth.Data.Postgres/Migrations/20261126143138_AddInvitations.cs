using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroundUp.Auth.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AcceptedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthInvitations_AuthRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AuthRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuthInvitations_AuthTenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "AuthTenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuthInvitations_AuthUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AuthUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthInvitations_RoleId",
                table: "AuthInvitations",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthInvitations_TenantId_NormalizedEmail_Pending",
                table: "AuthInvitations",
                columns: new[] { "TenantId", "NormalizedEmail" },
                unique: true,
                filter: "\"Status\" = 0 AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_AuthInvitations_TenantId_UserId",
                table: "AuthInvitations",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthInvitations_TokenHash",
                table: "AuthInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthInvitations_UserId",
                table: "AuthInvitations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthInvitations");
        }
    }
}
