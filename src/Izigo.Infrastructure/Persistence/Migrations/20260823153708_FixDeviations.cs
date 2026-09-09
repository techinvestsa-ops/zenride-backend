using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Izigo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixDeviations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Phone",
                table: "OtpRecords",
                newName: "Identifier");

            migrationBuilder.RenameColumn(
                name: "Token",
                table: "BiometricTokens",
                newName: "TokenHash");

            migrationBuilder.AddColumn<string>(
                name: "LastIpAddress",
                table: "UserDevices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastLocation",
                table: "UserDevices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "IdempotencyRecords",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastIpAddress",
                table: "UserDevices");

            migrationBuilder.DropColumn(
                name: "LastLocation",
                table: "UserDevices");

            migrationBuilder.RenameColumn(
                name: "Identifier",
                table: "OtpRecords",
                newName: "Phone");

            migrationBuilder.RenameColumn(
                name: "TokenHash",
                table: "BiometricTokens",
                newName: "Token");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "IdempotencyRecords",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
