using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Izigo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchA4Finance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                table: "DriverWallets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LockReason",
                table: "DriverWallets",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsLocked",
                table: "DriverWallets");

            migrationBuilder.DropColumn(
                name: "LockReason",
                table: "DriverWallets");
        }
    }
}
