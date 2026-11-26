using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroundUp.Auth.Data.Postgres.Migrations
{
    /// <summary>
    /// GU-68: pending users and memberships, and the role-assignment source.
    /// <list type="bullet">
    /// <item><c>AuthUserTenants.IsActive</c> becomes <c>Status</c> (Active = 0, Inactive = 1, Pending = 2).
    /// Existing rows map true → Active, false → Inactive.</item>
    /// <item><c>ExternalUserId</c> becomes nullable on users and memberships (a pending user has
    /// not signed in). It stays unique among non-null values.</item>
    /// <item><c>AuthUsers.MergedIntoUserId</c> records a pending user merged into another user.</item>
    /// <item><c>AuthUserRoles.Source</c> records why a role was assigned. SuperAdmin and the
    /// built-in TenantAdmin rows become Provisioning (4); the rest Manual (0). The unique index
    /// gains the source, so one role can be held from several sources.</item>
    /// <item>An expression index on <c>lower("Email")</c> for case-insensitive pending-user lookups.</item>
    /// </list>
    /// <c>Down</c> loses data that the old shape cannot hold: pending users and memberships are
    /// deleted, and duplicate-source role rows are reduced to one.
    /// </summary>
    public partial class AddPendingMembersAndRoleSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuthUsers_ExternalUserId",
                table: "AuthUsers");

            migrationBuilder.DropIndex(
                name: "IX_AuthUserRoles_UserId_RoleId_TenantId",
                table: "AuthUserRoles");

            // Membership status, carried over from IsActive before the old column goes.
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "AuthUserTenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE \"AuthUserTenants\" SET \"Status\" = 1 WHERE NOT \"IsActive\";");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "AuthUserTenants");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalUserId",
                table: "AuthUserTenants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "ExternalUserId",
                table: "AuthUsers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoUserId",
                table: "AuthUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "AuthUserRoles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // SuperAdmin (RoleType System = 0) and the built-in TenantAdmin were granted by the
            // SuperAdmin bootstrap and organization creation: source Provisioning (4).
            migrationBuilder.Sql(
                "UPDATE \"AuthUserRoles\" ur SET \"Source\" = 4 FROM \"AuthRoles\" r " +
                "WHERE ur.\"RoleId\" = r.\"Id\" " +
                "AND ((r.\"RoleType\" = 0 AND r.\"Name\" = 'SuperAdmin') " +
                "OR (r.\"IsSystem\" AND r.\"Name\" = 'TenantAdmin'));");

            migrationBuilder.CreateIndex(
                name: "IX_AuthUsers_ExternalUserId",
                table: "AuthUsers",
                column: "ExternalUserId",
                unique: true,
                filter: "\"ExternalUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuthUserRoles_UserId_RoleId_TenantId_Source",
                table: "AuthUserRoles",
                columns: new[] { "UserId", "RoleId", "TenantId", "Source" },
                unique: true);

            // EF cannot model expression indexes. Emails are not unique.
            migrationBuilder.Sql("CREATE INDEX \"IX_AuthUsers_Email_Lower\" ON \"AuthUsers\" (lower(\"Email\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AuthUsers_Email_Lower\";");

            migrationBuilder.DropIndex(
                name: "IX_AuthUsers_ExternalUserId",
                table: "AuthUsers");

            migrationBuilder.DropIndex(
                name: "IX_AuthUserRoles_UserId_RoleId_TenantId_Source",
                table: "AuthUserRoles");

            // The old shape has no pending users: delete them (memberships and roles cascade),
            // then any membership still without an identity.
            migrationBuilder.Sql("DELETE FROM \"AuthUsers\" WHERE \"ExternalUserId\" IS NULL;");
            migrationBuilder.Sql("DELETE FROM \"AuthUserTenants\" WHERE \"ExternalUserId\" IS NULL;");

            // One row per (user, role, tenant) for the old unique index.
            migrationBuilder.Sql(
                "DELETE FROM \"AuthUserRoles\" a USING \"AuthUserRoles\" b " +
                "WHERE a.\"UserId\" = b.\"UserId\" AND a.\"RoleId\" = b.\"RoleId\" AND a.\"TenantId\" = b.\"TenantId\" " +
                "AND (a.\"Source\" > b.\"Source\" OR (a.\"Source\" = b.\"Source\" AND a.\"Id\" > b.\"Id\"));");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "AuthUserTenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("UPDATE \"AuthUserTenants\" SET \"IsActive\" = (\"Status\" = 0);");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "AuthUserTenants");

            migrationBuilder.DropColumn(
                name: "MergedIntoUserId",
                table: "AuthUsers");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "AuthUserRoles");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalUserId",
                table: "AuthUserTenants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ExternalUserId",
                table: "AuthUsers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthUsers_ExternalUserId",
                table: "AuthUsers",
                column: "ExternalUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthUserRoles_UserId_RoleId_TenantId",
                table: "AuthUserRoles",
                columns: new[] { "UserId", "RoleId", "TenantId" },
                unique: true);
        }
    }
}
