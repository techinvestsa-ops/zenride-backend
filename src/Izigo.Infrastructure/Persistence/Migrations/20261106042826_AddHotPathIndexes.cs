using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Izigo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHotPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RiderId",
                table: "Trips",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Market",
                table: "Trips",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "DriverId",
                table: "Trips",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "JobId",
                table: "DriverLocationPoints",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DriverId",
                table: "DriverLocationPoints",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DriverId_JobState",
                table: "Trips",
                columns: new[] { "DriverId", "JobState" });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_Market_JobState_CreatedAt",
                table: "Trips",
                columns: new[] { "Market", "JobState", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_RiderId_CreatedAt",
                table: "Trips",
                columns: new[] { "RiderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverProfiles_IsOnline_LastLocationAt",
                table: "DriverProfiles",
                columns: new[] { "IsOnline", "LastLocationAt" })
                .Annotation("SqlServer:Include", new[] { "LastLat", "LastLng" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverLocationPoints_DriverId_RecordedAt",
                table: "DriverLocationPoints",
                columns: new[] { "DriverId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverLocationPoints_JobId",
                table: "DriverLocationPoints",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverLocationPoints_RecordedAt",
                table: "DriverLocationPoints",
                column: "RecordedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_DriverId_JobState",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_Market_JobState_CreatedAt",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_RiderId_CreatedAt",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_DriverProfiles_IsOnline_LastLocationAt",
                table: "DriverProfiles");

            migrationBuilder.DropIndex(
                name: "IX_DriverLocationPoints_DriverId_RecordedAt",
                table: "DriverLocationPoints");

            migrationBuilder.DropIndex(
                name: "IX_DriverLocationPoints_JobId",
                table: "DriverLocationPoints");

            migrationBuilder.DropIndex(
                name: "IX_DriverLocationPoints_RecordedAt",
                table: "DriverLocationPoints");

            migrationBuilder.AlterColumn<string>(
                name: "RiderId",
                table: "Trips",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Market",
                table: "Trips",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "DriverId",
                table: "Trips",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "JobId",
                table: "DriverLocationPoints",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DriverId",
                table: "DriverLocationPoints",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
