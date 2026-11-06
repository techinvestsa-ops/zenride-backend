using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Izigo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkDriverWalletsToProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Wallets were created keyed by the driver's user id but never linked
            // from the profile, so completed non-cash trips credited nobody and
            // cash-trip commission was never recorded. Link them.
            migrationBuilder.Sql(@"
UPDATE dp
SET    dp.DriverWalletId = w.Id
FROM   DriverProfiles dp
JOIN   DriverWallets  w ON w.DriverId = dp.UserId
WHERE  dp.DriverWalletId IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
