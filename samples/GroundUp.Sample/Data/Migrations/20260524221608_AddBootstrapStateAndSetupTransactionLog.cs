using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroundUp.Sample.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBootstrapStateAndSetupTransactionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BootstrapState",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BootstrapState", x => x.Id);
                    table.CheckConstraint("CK_BootstrapState_Singleton", "\"Id\" = '00000000-0000-0000-0000-000000000001'");
                });

            migrationBuilder.CreateTable(
                name: "SetupTransactionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExternalUserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetupTransactionLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BootstrapState_Id",
                table: "BootstrapState",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SetupTransactionLogs_CreatedAt",
                table: "SetupTransactionLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SetupTransactionLogs_Operation_Stage",
                table: "SetupTransactionLogs",
                columns: new[] { "Operation", "Stage" });

            // Seed the singleton BootstrapState row (Requirement 5.3)
            migrationBuilder.InsertData(
                table: "BootstrapState",
                columns: new[] { "Id", "IsComplete", "CompletedAt", "CompletedBy", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), false, null!, null!, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), "migration", null!, null! });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BootstrapState");

            migrationBuilder.DropTable(
                name: "SetupTransactionLogs");
        }
    }
}
