using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Izigo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigAndBannerEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppPages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentFormat = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Banners",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeepLink = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Placement = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Banners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OnboardingSlides",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Audience = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Subtitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingSlides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CurrencySymbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultMapCenterLat = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DefaultMapCenterLng = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VerticalsEnabledJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaymentMethodsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SupportPhone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SosPhone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CancellationPolicyJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferralEnabled = table.Column<bool>(type: "bit", nullable: false),
                    WalletEnabled = table.Column<bool>(type: "bit", nullable: false),
                    TippingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MinAppVersionIos = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MinAppVersionAndroid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LatestAppVersionIos = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LatestAppVersionAndroid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IosStoreUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AndroidStoreUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaintenanceModeRider = table.Column<bool>(type: "bit", nullable: false),
                    MaintenanceModeDriver = table.Column<bool>(type: "bit", nullable: false),
                    MaintenanceMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FeatureFlagsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OfferTimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    SearchRadiusM = table.Column<int>(type: "int", nullable: false),
                    DispatchStrategy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MaxConcurrentOffers = table.Column<int>(type: "int", nullable: false),
                    LocationPingOnTripSeconds = table.Column<int>(type: "int", nullable: false),
                    LocationPingIdleSeconds = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformConfigs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppPages");

            migrationBuilder.DropTable(
                name: "Banners");

            migrationBuilder.DropTable(
                name: "OnboardingSlides");

            migrationBuilder.DropTable(
                name: "PlatformConfigs");
        }
    }
}
