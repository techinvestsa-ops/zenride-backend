using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Izigo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKycClaimAndRejectionReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicationRejectionReason",
                table: "DriverOnboardings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "DriverOnboardings",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApplicationRejectionReason",
                table: "DriverOnboardings");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "DriverOnboardings");
        }
    }
}
