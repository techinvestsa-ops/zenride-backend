using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Izigo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FareTrafficAndAutoPromo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TrafficDelayMin",
                table: "FareRules",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<decimal>(
                name: "TrafficPercent",
                table: "FareRules",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 8m);

            migrationBuilder.AddColumn<bool>(
                name: "AutoApply",
                table: "Coupons",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrafficDelayMin",
                table: "FareRules");

            migrationBuilder.DropColumn(
                name: "TrafficPercent",
                table: "FareRules");

            migrationBuilder.DropColumn(
                name: "AutoApply",
                table: "Coupons");
        }
    }
}
