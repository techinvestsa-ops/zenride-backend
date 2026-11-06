using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Izigo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverDestinationFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DestinationLabel",
                table: "DriverProfiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DestinationLat",
                table: "DriverProfiles",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DestinationLng",
                table: "DriverProfiles",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DestinationSetAt",
                table: "DriverProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DestinationUsesOn",
                table: "DriverProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DestinationUsesToday",
                table: "DriverProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DestinationLabel",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "DestinationLat",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "DestinationLng",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "DestinationSetAt",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "DestinationUsesOn",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "DestinationUsesToday",
                table: "DriverProfiles");
        }
    }
}
