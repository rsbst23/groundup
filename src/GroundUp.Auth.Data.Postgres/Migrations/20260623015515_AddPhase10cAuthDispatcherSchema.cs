using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroundUp.Auth.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase10cAuthDispatcherSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodeVerifier",
                table: "AuthFlowStates",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OrganizationName",
                table: "AuthFlowStates",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RedirectUri",
                table: "AuthFlowStates",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StateToken",
                table: "AuthFlowStates",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_AuthFlowStates_StateToken",
                table: "AuthFlowStates",
                column: "StateToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthUsers_ExternalUserId",
                table: "AuthUsers",
                column: "ExternalUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuthFlowStates_StateToken",
                table: "AuthFlowStates");

            migrationBuilder.DropIndex(
                name: "IX_AuthUsers_ExternalUserId",
                table: "AuthUsers");

            migrationBuilder.DropColumn(
                name: "CodeVerifier",
                table: "AuthFlowStates");

            migrationBuilder.DropColumn(
                name: "OrganizationName",
                table: "AuthFlowStates");

            migrationBuilder.DropColumn(
                name: "RedirectUri",
                table: "AuthFlowStates");

            migrationBuilder.DropColumn(
                name: "StateToken",
                table: "AuthFlowStates");
        }
    }
}
