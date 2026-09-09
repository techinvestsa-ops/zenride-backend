using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Izigo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchA5Growth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferralConfigJson",
                table: "PlatformConfigs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferralConfigJson",
                table: "PlatformConfigs");
        }
    }
}
