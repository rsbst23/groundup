using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroundUp.Auth.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleNameIndexAndTenantDefaultRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // GU-95: role names are unique per tenant ignoring case. Stop with a clear message if
            // existing data already has duplicates, instead of failing on the index (nothing is renamed).
            migrationBuilder.Sql(@"
DO $$
DECLARE duplicate RECORD;
BEGIN
    SELECT ""TenantId"", lower(""Name"") AS name INTO duplicate
    FROM ""AuthRoles""
    GROUP BY ""TenantId"", lower(""Name"")
    HAVING count(*) > 1
    LIMIT 1;
    IF FOUND THEN
        RAISE EXCEPTION 'Duplicate role name ""%"" (ignoring case) in tenant %. Rename or remove one of them, then apply this migration again.', duplicate.name, duplicate.""TenantId"";
    END IF;
END $$;");

            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX ""IX_AuthRoles_TenantId_LowerName"" ON ""AuthRoles"" (""TenantId"", lower(""Name""));");

            migrationBuilder.DropForeignKey(
                name: "FK_AuthUserRoles_AuthRoles_RoleId",
                table: "AuthUserRoles");

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultRoleId",
                table: "AuthTenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthTenants_DefaultRoleId",
                table: "AuthTenants",
                column: "DefaultRoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_AuthTenants_AuthRoles_DefaultRoleId",
                table: "AuthTenants",
                column: "DefaultRoleId",
                principalTable: "AuthRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AuthUserRoles_AuthRoles_RoleId",
                table: "AuthUserRoles",
                column: "RoleId",
                principalTable: "AuthRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_AuthRoles_TenantId_LowerName"";");

            migrationBuilder.DropForeignKey(
                name: "FK_AuthTenants_AuthRoles_DefaultRoleId",
                table: "AuthTenants");

            migrationBuilder.DropForeignKey(
                name: "FK_AuthUserRoles_AuthRoles_RoleId",
                table: "AuthUserRoles");

            migrationBuilder.DropIndex(
                name: "IX_AuthTenants_DefaultRoleId",
                table: "AuthTenants");

            migrationBuilder.DropColumn(
                name: "DefaultRoleId",
                table: "AuthTenants");

            migrationBuilder.AddForeignKey(
                name: "FK_AuthUserRoles_AuthRoles_RoleId",
                table: "AuthUserRoles",
                column: "RoleId",
                principalTable: "AuthRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
