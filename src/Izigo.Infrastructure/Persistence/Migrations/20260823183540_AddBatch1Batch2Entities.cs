using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Izigo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBatch1Batch2Entities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CancellationReasons",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Audience = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoRideRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RiderId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PickupLat = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PickupLng = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PickupLabel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DropoffLat = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DropoffLng = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DropoffLabel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SeatsNeeded = table.Column<int>(type: "int", nullable: false),
                    DepartureWindowFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DepartureWindowTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MatchedListingId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MatchedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoRideRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceDiagnostics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DeviceId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BatteryLevel = table.Column<int>(type: "int", nullable: true),
                    LocationPermission = table.Column<bool>(type: "bit", nullable: true),
                    BackgroundPermission = table.Column<bool>(type: "bit", nullable: true),
                    MockLocationDetected = table.Column<bool>(type: "bit", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceDiagnostics", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CancellationReasons");

            migrationBuilder.DropTable(
                name: "CoRideRequests");

            migrationBuilder.DropTable(
                name: "DeviceDiagnostics");
        }
    }
}
