using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Izigo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchA1Admin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AbsoluteCreatedAt",
                table: "StaffRefreshTokens",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "SessionToken",
                table: "StaffRefreshTokens",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DispatchConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OfferTimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    SearchRadiusM = table.Column<int>(type: "int", nullable: false),
                    Strategy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MaxConcurrentOffers = table.Column<int>(type: "int", nullable: false),
                    LocationPingOnTripS = table.Column<int>(type: "int", nullable: false),
                    LocationPingIdleS = table.Column<int>(type: "int", nullable: false),
                    UpdatedByStaffId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeatureFlags",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    RolloutPct = table.Column<int>(type: "int", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedByStaffId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Integrations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsConnected = table.Column<bool>(type: "bit", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    KeyLastRotatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Market = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Integrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffInvites",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RoleKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MarketsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvitedByStaffId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsUsed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffInvites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffPasswordResets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StaffId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsUsed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffPasswordResets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TwoFaChallenges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StaffId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChallengeTokenHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsUsed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwoFaChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TwoFaRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StaffId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsUsed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwoFaRecoveryCodes", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DispatchConfigs");

            migrationBuilder.DropTable(
                name: "FeatureFlags");

            migrationBuilder.DropTable(
                name: "Integrations");

            migrationBuilder.DropTable(
                name: "StaffInvites");

            migrationBuilder.DropTable(
                name: "StaffPasswordResets");

            migrationBuilder.DropTable(
                name: "TwoFaChallenges");

            migrationBuilder.DropTable(
                name: "TwoFaRecoveryCodes");

            migrationBuilder.DropColumn(
                name: "AbsoluteCreatedAt",
                table: "StaffRefreshTokens");

            migrationBuilder.DropColumn(
                name: "SessionToken",
                table: "StaffRefreshTokens");
        }
    }
}
