using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroundUp.Sample.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SettingGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SettingLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettingLevels_SettingLevels_ParentId",
                        column: x => x.ParentId,
                        principalTable: "SettingLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SettingDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DataType = table.Column<int>(type: "integer", nullable: false),
                    DefaultValue = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Placeholder = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsReadOnly = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    AllowMultiple = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsEncrypted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsSecret = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    MinValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MaxValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MinLength = table.Column<int>(type: "integer", nullable: true),
                    MaxLength = table.Column<int>(type: "integer", nullable: true),
                    RegexPattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ValidationMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DependsOnKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DependsOnOperator = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DependsOnValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CustomValidatorType = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettingDefinitions_SettingGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "SettingGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SettingDefinitionLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingLevelId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingDefinitionLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettingDefinitionLevels_SettingDefinitions_SettingDefinitio~",
                        column: x => x.SettingDefinitionId,
                        principalTable: "SettingDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SettingDefinitionLevels_SettingLevels_SettingLevelId",
                        column: x => x.SettingLevelId,
                        principalTable: "SettingLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SettingOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ParentOptionValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettingOptions_SettingDefinitions_SettingDefinitionId",
                        column: x => x.SettingDefinitionId,
                        principalTable: "SettingDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SettingValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LevelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettingValues_SettingDefinitions_SettingDefinitionId",
                        column: x => x.SettingDefinitionId,
                        principalTable: "SettingDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SettingValues_SettingLevels_LevelId",
                        column: x => x.LevelId,
                        principalTable: "SettingLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SettingDefinitionLevels_SettingDefinitionId_SettingLevelId",
                table: "SettingDefinitionLevels",
                columns: new[] { "SettingDefinitionId", "SettingLevelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettingDefinitionLevels_SettingLevelId",
                table: "SettingDefinitionLevels",
                column: "SettingLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_SettingDefinitions_GroupId",
                table: "SettingDefinitions",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SettingDefinitions_Key",
                table: "SettingDefinitions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettingGroups_Key",
                table: "SettingGroups",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettingLevels_ParentId",
                table: "SettingLevels",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_SettingOptions_SettingDefinitionId",
                table: "SettingOptions",
                column: "SettingDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_SettingValues_LevelId",
                table: "SettingValues",
                column: "LevelId");

            migrationBuilder.CreateIndex(
                name: "IX_SettingValues_SettingDefinitionId",
                table: "SettingValues",
                column: "SettingDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_SettingValues_SettingDefinitionId_LevelId_ScopeId",
                table: "SettingValues",
                columns: new[] { "SettingDefinitionId", "LevelId", "ScopeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SettingDefinitionLevels");

            migrationBuilder.DropTable(
                name: "SettingOptions");

            migrationBuilder.DropTable(
                name: "SettingValues");

            migrationBuilder.DropTable(
                name: "SettingDefinitions");

            migrationBuilder.DropTable(
                name: "SettingLevels");

            migrationBuilder.DropTable(
                name: "SettingGroups");
        }
    }
}
