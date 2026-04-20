using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroundUp.Sample.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTestProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiscountPercent",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsUrgent",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ItemCount",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ShipDate",
                table: "Orders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShippingWeight",
                table: "Orders",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<long>(
                name: "TrackingNumber",
                table: "Orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsUrgent",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ItemCount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShipDate",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingWeight",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TrackingNumber",
                table: "Orders");
        }
    }
}
