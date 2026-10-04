using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Izigo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoRideBoardAlight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AlightLat",
                table: "CoRideBookings",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AlightLng",
                table: "CoRideBookings",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AlightedAt",
                table: "CoRideBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BoardLat",
                table: "CoRideBookings",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BoardLng",
                table: "CoRideBookings",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BoardedAt",
                table: "CoRideBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoardingCode",
                table: "CoRideBookings",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlightLat",
                table: "CoRideBookings");

            migrationBuilder.DropColumn(
                name: "AlightLng",
                table: "CoRideBookings");

            migrationBuilder.DropColumn(
                name: "AlightedAt",
                table: "CoRideBookings");

            migrationBuilder.DropColumn(
                name: "BoardLat",
                table: "CoRideBookings");

            migrationBuilder.DropColumn(
                name: "BoardLng",
                table: "CoRideBookings");

            migrationBuilder.DropColumn(
                name: "BoardedAt",
                table: "CoRideBookings");

            migrationBuilder.DropColumn(
                name: "BoardingCode",
                table: "CoRideBookings");
        }
    }
}
