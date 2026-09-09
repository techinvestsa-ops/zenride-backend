-- ============================================================
--  Izigo API – Seed Data
--  Run AFTER izigo-schema.sql on a fresh database.
--  Idempotent: every INSERT is guarded by IF NOT EXISTS.
--
--  Default super-admin credentials
--    Email   : admin@zenride.app
--    Password: Admin@Zenride2025!
--              (PBKDF2-SHA512, 100 000 iterations)
--  !! Change this password immediately after first login !!
-- ============================================================
SET NOCOUNT ON;
GO

-- ── PlatformConfigs ───────────────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM [PlatformConfigs] WHERE [Market] = 'ci')
INSERT INTO [PlatformConfigs] (
    [Id], [Market], [Currency], [CurrencySymbol], [Country],
    [DefaultMapCenterLat], [DefaultMapCenterLng],
    [VerticalsEnabledJson], [PaymentMethodsJson],
    [SupportPhone], [SosPhone], [CancellationPolicyJson],
    [ReferralEnabled], [WalletEnabled], [TippingEnabled],
    [MinAppVersionIos], [MinAppVersionAndroid],
    [LatestAppVersionIos], [LatestAppVersionAndroid],
    [IosStoreUrl], [AndroidStoreUrl], [ReferralConfigJson],
    [MaintenanceModeRider], [MaintenanceModeDriver], [MaintenanceMessage],
    [FeatureFlagsJson],
    [OfferTimeoutSeconds], [SearchRadiusM], [DispatchStrategy],
    [MaxConcurrentOffers], [LocationPingOnTripSeconds], [LocationPingIdleSeconds],
    [CreatedAt], [UpdatedAt]
) VALUES (
    'pc_ci', 'ci', 'XOF', 'F CFA', 'CI',
    5.3599, -3.9970,
    '["ride","co_ride","package"]', '["cash","wallet","orange_money","moov_money","wave"]',
    '', '', '{}',
    1, 1, 0,
    '1.0.0', '1.0.0', '1.0.0', '1.0.0',
    NULL, NULL, NULL,
    0, 0, NULL,
    '{"co_ride_enabled":{"enabled":true},"package_enabled":{"enabled":true},"tipping":{"enabled":false},"wallet_transfer":{"enabled":true},"instant_payout":{"enabled":false},"referrals":{"enabled":true},"masked_calls":{"enabled":true},"biometric_login":{"enabled":true},"scheduled_rides":{"enabled":false}}',
    15, 5000, 'broadcast', 5, 5, 20,
    GETUTCDATE(), GETUTCDATE()
);

IF NOT EXISTS (SELECT 1 FROM [PlatformConfigs] WHERE [Market] = 'ng')
INSERT INTO [PlatformConfigs] (
    [Id], [Market], [Currency], [CurrencySymbol], [Country],
    [DefaultMapCenterLat], [DefaultMapCenterLng],
    [VerticalsEnabledJson], [PaymentMethodsJson],
    [SupportPhone], [SosPhone], [CancellationPolicyJson],
    [ReferralEnabled], [WalletEnabled], [TippingEnabled],
    [MinAppVersionIos], [MinAppVersionAndroid],
    [LatestAppVersionIos], [LatestAppVersionAndroid],
    [IosStoreUrl], [AndroidStoreUrl], [ReferralConfigJson],
    [MaintenanceModeRider], [MaintenanceModeDriver], [MaintenanceMessage],
    [FeatureFlagsJson],
    [OfferTimeoutSeconds], [SearchRadiusM], [DispatchStrategy],
    [MaxConcurrentOffers], [LocationPingOnTripSeconds], [LocationPingIdleSeconds],
    [CreatedAt], [UpdatedAt]
) VALUES (
    'pc_ng', 'ng', 'NGN', N'₦', 'NG',
    6.4541, 3.3947,
    '["ride","co_ride","package"]', '["cash","wallet","mtn_momo","wave"]',
    '', '', '{}',
    1, 1, 0,
    '1.0.0', '1.0.0', '1.0.0', '1.0.0',
    NULL, NULL, NULL,
    0, 0, NULL,
    '{"co_ride_enabled":{"enabled":true},"package_enabled":{"enabled":true},"tipping":{"enabled":false},"wallet_transfer":{"enabled":true},"instant_payout":{"enabled":false},"referrals":{"enabled":true},"masked_calls":{"enabled":true},"biometric_login":{"enabled":true},"scheduled_rides":{"enabled":false}}',
    15, 5000, 'broadcast', 5, 5, 20,
    GETUTCDATE(), GETUTCDATE()
);
GO

-- ── Staff — Super Admin ────────────────────────────────────────────────────────
-- Password hash encodes: Admin@Zenride2025! / 100 000 PBKDF2-SHA512 iterations
-- Format: {iterations}.{base64_salt}.{base64_hash}

IF NOT EXISTS (SELECT 1 FROM [Staff] WHERE [RoleKey] = 'super_admin')
INSERT INTO [Staff] (
    [Id], [Name], [Email], [Phone], [PasswordHash],
    [RoleKey], [Status], [Markets],
    [TwoFaEnabled], [TwoFaSecret], [MustChangePassword],
    [SuspensionReason], [SuspendedUntil],
    [GrantedPermissions], [RevokedPermissions],
    [LastActiveAt], [CreatedAt], [UpdatedAt], [CreatedBy], [UpdatedBy]
) VALUES (
    'stf_superadmin_seed001',
    'Super Admin',
    'admin@zenride.app',
    NULL,
    '100000.3lEjglfciJYv8BpgNtFinA==.J3h2fisTqEcgWq9Nx0S46JWEQ6c/JpH7FNtpadMKoy4=',
    'super_admin',
    0,              -- StaffStatus.Active = 0
    '["ci","ng"]',
    0, NULL, 1,     -- TwoFaEnabled = false, MustChangePassword = true
    NULL, NULL,
    '[]', '[]',
    NULL, GETUTCDATE(), GETUTCDATE(), NULL, NULL
);
GO

-- ── FareRules — CI market only ────────────────────────────────────────────────
-- ServiceClass enum: ZenCar=0  ZenBike=1  ZenCoRide=2  PackageSmall=3  PackageLarge=4
-- NG rules are intentionally omitted until market filtering is implemented (TODO 2.3).

IF NOT EXISTS (SELECT 1 FROM [FareRules] WHERE [Market] = 'ci')
INSERT INTO [FareRules] (
    [Id], [ServiceClass], [Base], [PerKm], [PerMin], [Minimum],
    [WaitingPerMin], [CancellationFee], [IsActive], [Version],
    [EffectiveFrom], [UpdatedByStaffId], [Market], [CreatedAt], [UpdatedAt]
) VALUES
-- ZenCar: standard 4-seat sedan for Abidjan
('fr_ci_zencar',    0, 500, 250, 15, 1200, 20, 500, 1, 1, GETUTCDATE(), NULL, 'ci', GETUTCDATE(), GETUTCDATE()),
-- ZenBike: motorbike, faster for short hops
('fr_ci_zenbike',   1, 300, 180, 10,  800, 15, 300, 1, 1, GETUTCDATE(), NULL, 'ci', GETUTCDATE(), GETUTCDATE()),
-- ZenCoRide: shared ride, cheaper per-seat rate
('fr_ci_zencoride', 2, 250, 190, 10,  600, 10, 200, 1, 1, GETUTCDATE(), NULL, 'ci', GETUTCDATE(), GETUTCDATE()),
-- PackageSmall: parcels up to ~5 kg (no waiting/cancellation fee)
('fr_ci_pkgsmall',  3, 400, 200, 12,  900,  0,   0, 1, 1, GETUTCDATE(), NULL, 'ci', GETUTCDATE(), GETUTCDATE()),
-- PackageLarge: bulky items, van or large bike (no waiting/cancellation fee)
('fr_ci_pkglarge',  4, 600, 280, 12, 1500,  0,   0, 1, 1, GETUTCDATE(), NULL, 'ci', GETUTCDATE(), GETUTCDATE());
GO

-- ── DispatchConfigs ────────────────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM [DispatchConfigs] WHERE [Market] = 'ci')
INSERT INTO [DispatchConfigs] (
    [Id], [Market], [OfferTimeoutSeconds], [SearchRadiusM], [Strategy],
    [MaxConcurrentOffers], [LocationPingOnTripS], [LocationPingIdleS],
    [UpdatedByStaffId], [UpdatedAt], [CreatedAt]
) VALUES ('dsc_ci', 'ci', 15, 3000, 'broadcast', 3, 5, 20, NULL, GETUTCDATE(), GETUTCDATE());

IF NOT EXISTS (SELECT 1 FROM [DispatchConfigs] WHERE [Market] = 'ng')
INSERT INTO [DispatchConfigs] (
    [Id], [Market], [OfferTimeoutSeconds], [SearchRadiusM], [Strategy],
    [MaxConcurrentOffers], [LocationPingOnTripS], [LocationPingIdleS],
    [UpdatedByStaffId], [UpdatedAt], [CreatedAt]
) VALUES ('dsc_ng', 'ng', 15, 3000, 'broadcast', 3, 5, 20, NULL, GETUTCDATE(), GETUTCDATE());
GO

-- ── CommissionConfigs ──────────────────────────────────────────────────────────
-- 25% platform commission + 3.5% ChopMonie bonus pool

IF NOT EXISTS (SELECT 1 FROM [CommissionConfigs] WHERE [Market] = 'ci')
INSERT INTO [CommissionConfigs] (
    [Id], [CommissionRate], [BonusRate], [BonusLabel],
    [CashSettlementCap], [VerticalOverridesJson], [Version],
    [EffectiveFrom], [UpdatedByStaffId], [Market], [CreatedAt], [UpdatedAt]
) VALUES ('cmc_ci', 0.25, 0.035, 'ChopMonie Bonus', 10000, '{}', 1, GETUTCDATE(), NULL, 'ci', GETUTCDATE(), GETUTCDATE());

IF NOT EXISTS (SELECT 1 FROM [CommissionConfigs] WHERE [Market] = 'ng')
INSERT INTO [CommissionConfigs] (
    [Id], [CommissionRate], [BonusRate], [BonusLabel],
    [CashSettlementCap], [VerticalOverridesJson], [Version],
    [EffectiveFrom], [UpdatedByStaffId], [Market], [CreatedAt], [UpdatedAt]
) VALUES ('cmc_ng', 0.25, 0.035, 'ChopMonie Bonus', 5000, '{}', 1, GETUTCDATE(), NULL, 'ng', GETUTCDATE(), GETUTCDATE());
GO

-- ── FeatureFlags (10 flags × 2 markets = 20 rows) ─────────────────────────────

DECLARE @flags TABLE ([Key] nvarchar(200), [Description] nvarchar(500), [Enabled] bit);
INSERT INTO @flags ([Key], [Description], [Enabled]) VALUES
('co_ride_enabled', 'Enable Co-Ride (shared trips) vertical',          1),
('package_enabled', 'Enable Package Delivery vertical',                 1),
('food_enabled',    'Enable Food Delivery vertical (future)',           0),
('scheduled_rides', 'Allow riders to schedule trips in advance',        0),
('tipping',         'Allow riders to tip drivers after a trip',          0),
('wallet_transfer', 'Allow wallet-to-wallet transfers between riders',  1),
('instant_payout',  'Enable instant driver payout (higher fee)',        0),
('referrals',       'Enable referral programme',                        1),
('masked_calls',    'Mask phone numbers via proxy when calling driver', 1),
('biometric_login', 'Allow biometric (fingerprint/face) login',         1);

INSERT INTO [FeatureFlags]
    ([Id], [Key], [Description], [Enabled], [RolloutPct], [Scope], [Market], [UpdatedByStaffId], [UpdatedAt], [CreatedAt])
SELECT
    CONCAT('flg_', f.[Key], '_ci'),
    f.[Key], f.[Description], f.[Enabled], 100, 'global', 'ci', NULL, GETUTCDATE(), GETUTCDATE()
FROM @flags f
WHERE NOT EXISTS (
    SELECT 1 FROM [FeatureFlags] ff WHERE ff.[Key] = f.[Key] AND ff.[Market] = 'ci'
);

INSERT INTO [FeatureFlags]
    ([Id], [Key], [Description], [Enabled], [RolloutPct], [Scope], [Market], [UpdatedByStaffId], [UpdatedAt], [CreatedAt])
SELECT
    CONCAT('flg_', f.[Key], '_ng'),
    f.[Key], f.[Description], f.[Enabled], 100, 'global', 'ng', NULL, GETUTCDATE(), GETUTCDATE()
FROM @flags f
WHERE NOT EXISTS (
    SELECT 1 FROM [FeatureFlags] ff WHERE ff.[Key] = f.[Key] AND ff.[Market] = 'ng'
);
GO
