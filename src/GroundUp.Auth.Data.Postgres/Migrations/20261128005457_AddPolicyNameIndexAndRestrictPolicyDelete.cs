using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroundUp.Auth.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyNameIndexAndRestrictPolicyDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // GU-96: policy names are unique per tenant ignoring case. Stop with a clear message if
            // existing data already has duplicates, instead of failing on the index (nothing is renamed).
            migrationBuilder.Sql(@"
DO $$
DECLARE duplicate RECORD;
BEGIN
    SELECT ""TenantId"", lower(""Name"") AS name INTO duplicate
    FROM ""AuthPolicies""
    GROUP BY ""TenantId"", lower(""Name"")
    HAVING count(*) > 1
    LIMIT 1;
    IF FOUND THEN
        RAISE EXCEPTION 'Duplicate policy name ""%"" (ignoring case) in tenant %. Rename or remove one of them, then apply this migration again.', duplicate.name, duplicate.""TenantId"";
    END IF;
END $$;");

            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX ""IX_AuthPolicies_TenantId_LowerName"" ON ""AuthPolicies"" (""TenantId"", lower(""Name""));");

            // A policy used by a role cannot be deleted: a role attached while a delete runs makes the
            // delete fail instead of silently losing the link
            migrationBuilder.DropForeignKey(
                name: "FK_AuthRolePolicies_AuthPolicies_PolicyId",
                table: "AuthRolePolicies");

            migrationBuilder.AddForeignKey(
                name: "FK_AuthRolePolicies_AuthPolicies_PolicyId",
                table: "AuthRolePolicies",
                column: "PolicyId",
                principalTable: "AuthPolicies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_AuthPolicies_TenantId_LowerName"";");

            migrationBuilder.DropForeignKey(
                name: "FK_AuthRolePolicies_AuthPolicies_PolicyId",
                table: "AuthRolePolicies");

            migrationBuilder.AddForeignKey(
                name: "FK_AuthRolePolicies_AuthPolicies_PolicyId",
                table: "AuthRolePolicies",
                column: "PolicyId",
                principalTable: "AuthPolicies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
