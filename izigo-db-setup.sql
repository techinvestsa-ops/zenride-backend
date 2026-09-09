-- ============================================================
--  Izigo API - Full Database Setup Script
--  Generated: 2026-08-30 01:26 UTC
--
--  Usage:
--    sqlcmd -S <server> -d <database> -i izigo-db-setup.sql
--
--  Verified against: (localdb)\mssqllocaldb / IzigoDB
--  All 16 EF migrations + 11 Hangfire tables match.
--
--  Default super-admin credentials after seed:
--    Email   : admin@zenride.app
--    Password: Admin@Zenride2025!
--  !! Change this password immediately after first login !!
-- ============================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] nvarchar(450) NOT NULL,
        [ActorId] nvarchar(max) NOT NULL,
        [ActorName] nvarchar(max) NOT NULL,
        [Action] int NOT NULL,
        [TargetType] nvarchar(max) NULL,
        [TargetId] nvarchar(max) NULL,
        [IpAddress] nvarchar(max) NULL,
        [RequestId] nvarchar(max) NULL,
        [Reason] nvarchar(max) NULL,
        [BeforeJson] nvarchar(max) NULL,
        [AfterJson] nvarchar(max) NULL,
        [Market] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [BackgroundJobs] (
        [Id] nvarchar(450) NOT NULL,
        [Type] nvarchar(max) NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [Progress] decimal(18,2) NOT NULL,
        [ResultUrl] nvarchar(max) NULL,
        [Error] nvarchar(max) NULL,
        [InitiatedByStaffId] nvarchar(max) NULL,
        [CompletedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_BackgroundJobs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Broadcasts] (
        [Id] nvarchar(450) NOT NULL,
        [Audience] nvarchar(max) NOT NULL,
        [Channel] nvarchar(max) NOT NULL,
        [Title] nvarchar(max) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [DeepLink] nvarchar(max) NULL,
        [ScheduledAt] datetime2 NULL,
        [SentAt] datetime2 NULL,
        [DeliveredCount] int NOT NULL,
        [FailedCount] int NOT NULL,
        [OpenedPct] decimal(18,2) NOT NULL,
        [IsCancelled] bit NOT NULL,
        [SentByStaffId] nvarchar(max) NULL,
        [Market] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Broadcasts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [CommissionConfigs] (
        [Id] nvarchar(450) NOT NULL,
        [CommissionRate] decimal(18,2) NOT NULL,
        [BonusRate] decimal(18,2) NOT NULL,
        [BonusLabel] nvarchar(max) NOT NULL,
        [CashSettlementCap] bigint NOT NULL,
        [VerticalOverridesJson] nvarchar(max) NOT NULL,
        [Version] int NOT NULL,
        [EffectiveFrom] datetime2 NOT NULL,
        [UpdatedByStaffId] nvarchar(max) NULL,
        [Market] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CommissionConfigs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [CoRideListings] (
        [Id] nvarchar(450) NOT NULL,
        [DriverId] nvarchar(max) NOT NULL,
        [FromLat] decimal(18,2) NOT NULL,
        [FromLng] decimal(18,2) NOT NULL,
        [FromLabel] nvarchar(max) NOT NULL,
        [ToLat] decimal(18,2) NOT NULL,
        [ToLng] decimal(18,2) NOT NULL,
        [ToLabel] nvarchar(max) NOT NULL,
        [DepartureAt] datetime2 NOT NULL,
        [SeatsTotal] int NOT NULL,
        [SeatsTaken] int NOT NULL,
        [PricePerSeat] bigint NOT NULL,
        [ServiceFee] bigint NOT NULL,
        [Currency] nvarchar(max) NOT NULL,
        [IsEco] bit NOT NULL,
        [IsRecurring] bit NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [CancellationReason] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_CoRideListings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Coupons] (
        [Id] nvarchar(450) NOT NULL,
        [Code] nvarchar(max) NOT NULL,
        [Title] nvarchar(max) NOT NULL,
        [DiscountType] nvarchar(max) NOT NULL,
        [Value] bigint NOT NULL,
        [MaxDiscount] bigint NOT NULL,
        [MinOrder] bigint NOT NULL,
        [VerticalsJson] nvarchar(max) NOT NULL,
        [ZonesJson] nvarchar(max) NOT NULL,
        [FirstTripOnly] bit NOT NULL,
        [PerUserLimit] int NOT NULL,
        [TotalCap] int NOT NULL,
        [RedemptionCount] int NOT NULL,
        [StartsAt] datetime2 NULL,
        [ExpiresAt] datetime2 NULL,
        [IsActive] bit NOT NULL,
        [Market] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Coupons] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [DriverLocationPoints] (
        [Id] nvarchar(450) NOT NULL,
        [DriverId] nvarchar(max) NOT NULL,
        [Lat] decimal(18,2) NOT NULL,
        [Lng] decimal(18,2) NOT NULL,
        [Heading] decimal(18,2) NULL,
        [Speed] decimal(18,2) NULL,
        [Accuracy] decimal(18,2) NULL,
        [RecordedAt] datetime2 NOT NULL,
        [JobId] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DriverLocationPoints] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [DriverWallets] (
        [Id] nvarchar(450) NOT NULL,
        [DriverId] nvarchar(max) NOT NULL,
        [AvailableBalance] bigint NOT NULL,
        [PendingCashSettlement] bigint NOT NULL,
        [AdjustmentsTotal] bigint NOT NULL,
        [Currency] nvarchar(max) NOT NULL,
        [InstantWithdrawalEnabled] bit NOT NULL,
        [MinWithdrawal] bigint NOT NULL,
        [WithdrawalFee] bigint NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DriverWallets] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [FareRules] (
        [Id] nvarchar(450) NOT NULL,
        [ServiceClass] int NOT NULL,
        [Base] bigint NOT NULL,
        [PerKm] bigint NOT NULL,
        [PerMin] bigint NOT NULL,
        [Minimum] bigint NOT NULL,
        [WaitingPerMin] bigint NOT NULL,
        [CancellationFee] bigint NOT NULL,
        [IsActive] bit NOT NULL,
        [Version] int NOT NULL,
        [EffectiveFrom] datetime2 NOT NULL,
        [UpdatedByStaffId] nvarchar(max) NULL,
        [Market] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_FareRules] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [IdempotencyRecords] (
        [Id] nvarchar(450) NOT NULL,
        [Key] nvarchar(max) NOT NULL,
        [UserId] nvarchar(max) NOT NULL,
        [Endpoint] nvarchar(max) NOT NULL,
        [ResponseJson] nvarchar(max) NOT NULL,
        [StatusCode] int NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Incentives] (
        [Id] nvarchar(450) NOT NULL,
        [Title] nvarchar(max) NOT NULL,
        [Audience] nvarchar(max) NOT NULL,
        [ConditionJson] nvarchar(max) NOT NULL,
        [Reward] bigint NOT NULL,
        [Currency] nvarchar(max) NOT NULL,
        [BudgetCap] bigint NULL,
        [BudgetUsed] bigint NOT NULL,
        [IsActive] bit NOT NULL,
        [StartsAt] datetime2 NULL,
        [EndsAt] datetime2 NULL,
        [Market] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Incentives] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(max) NOT NULL,
        [Type] nvarchar(max) NOT NULL,
        [Title] nvarchar(max) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [ImageUrl] nvarchar(max) NULL,
        [DeepLink] nvarchar(max) NULL,
        [IsRead] bit NOT NULL,
        [Group] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [OtpRecords] (
        [Id] nvarchar(450) NOT NULL,
        [Phone] nvarchar(max) NOT NULL,
        [OtpToken] nvarchar(max) NOT NULL,
        [CodeHash] nvarchar(max) NOT NULL,
        [Purpose] int NOT NULL,
        [Role] nvarchar(max) NULL,
        [AttemptCount] int NOT NULL,
        [IsUsed] bit NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [IsNewUser] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_OtpRecords] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Packages] (
        [Id] nvarchar(450) NOT NULL,
        [TrackingId] nvarchar(max) NOT NULL,
        [SenderId] nvarchar(max) NOT NULL,
        [CourierId] nvarchar(max) NULL,
        [QuoteId] nvarchar(max) NOT NULL,
        [PickupLat] decimal(18,2) NOT NULL,
        [PickupLng] decimal(18,2) NOT NULL,
        [PickupLabel] nvarchar(max) NOT NULL,
        [DropoffLat] decimal(18,2) NOT NULL,
        [DropoffLng] decimal(18,2) NOT NULL,
        [DropoffLabel] nvarchar(max) NOT NULL,
        [RecipientName] nvarchar(max) NOT NULL,
        [RecipientPhone] nvarchar(max) NOT NULL,
        [Size] nvarchar(max) NOT NULL,
        [IsExpress] bit NOT NULL,
        [IsFragile] bit NOT NULL,
        [Description] nvarchar(max) NULL,
        [Instructions] nvarchar(max) NULL,
        [RecipientPays] bit NOT NULL,
        [DeclaredValue] bigint NULL,
        [Status] int NOT NULL,
        [ProofCode] nvarchar(max) NULL,
        [ProofPhotoUrl] nvarchar(max) NULL,
        [FailedDeliveryReason] nvarchar(max) NULL,
        [FareTotal] bigint NOT NULL,
        [Currency] nvarchar(max) NOT NULL,
        [PaymentMethod] int NOT NULL,
        [PaymentId] nvarchar(max) NULL,
        [Market] nvarchar(max) NOT NULL,
        [PickedUpAt] datetime2 NULL,
        [DeliveredAt] datetime2 NULL,
        [CancelledAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Packages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Payments] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(max) NOT NULL,
        [Purpose] nvarchar(max) NOT NULL,
        [ReferenceId] nvarchar(max) NULL,
        [Amount] bigint NOT NULL,
        [Currency] nvarchar(max) NOT NULL,
        [Method] int NOT NULL,
        [Status] int NOT NULL,
        [Gateway] nvarchar(max) NULL,
        [GatewayReference] nvarchar(max) NULL,
        [FailureReason] nvarchar(max) NULL,
        [IdempotencyKey] nvarchar(max) NULL,
        [CheckoutUrl] nvarchar(max) NULL,
        [UssdCode] nvarchar(max) NULL,
        [DeepLink] nvarchar(max) NULL,
        [OtpRequired] bit NOT NULL,
        [IsReconciled] bit NOT NULL,
        [ReconciliationNote] nvarchar(max) NULL,
        [Market] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Payouts] (
        [Id] nvarchar(450) NOT NULL,
        [DriverId] nvarchar(max) NOT NULL,
        [PayoutMethodId] nvarchar(max) NOT NULL,
        [GrossAmount] bigint NOT NULL,
        [Fee] bigint NOT NULL,
        [NetAmount] bigint NOT NULL,
        [Status] int NOT NULL,
        [ProviderReference] nvarchar(max) NULL,
        [FailureReason] nvarchar(max) NULL,
        [IdempotencyKey] nvarchar(max) NULL,
        [ApprovedBy] nvarchar(max) NULL,
        [ApprovedAt] datetime2 NULL,
        [RejectionReason] nvarchar(max) NULL,
        [Currency] nvarchar(max) NOT NULL,
        [Market] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Payouts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Quotes] (
        [Id] nvarchar(450) NOT NULL,
        [RiderId] nvarchar(max) NOT NULL,
        [Vertical] int NOT NULL,
        [PickupLat] decimal(18,2) NOT NULL,
        [PickupLng] decimal(18,2) NOT NULL,
        [PickupLabel] nvarchar(max) NOT NULL,
        [DropoffLat] decimal(18,2) NOT NULL,
        [DropoffLng] decimal(18,2) NOT NULL,
        [DropoffLabel] nvarchar(max) NOT NULL,
        [DistanceM] int NOT NULL,
        [DurationS] int NOT NULL,
        [EncodedPolyline] nvarchar(max) NULL,
        [OptionsJson] nvarchar(max) NOT NULL,
        [SurgeActive] bit NOT NULL,
        [SurgeMultiplier] decimal(18,2) NOT NULL,
        [SurgeReason] nvarchar(max) NULL,
        [PromoCode] nvarchar(max) NULL,
        [Currency] nvarchar(max) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [IsUsed] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Quotes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Staff] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Email] nvarchar(max) NOT NULL,
        [Phone] nvarchar(max) NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [RoleKey] nvarchar(max) NOT NULL,
        [Status] int NOT NULL,
        [Markets] nvarchar(max) NOT NULL,
        [TwoFaEnabled] bit NOT NULL,
        [TwoFaSecret] nvarchar(max) NULL,
        [MustChangePassword] bit NOT NULL,
        [SuspensionReason] nvarchar(max) NULL,
        [SuspendedUntil] datetime2 NULL,
        [GrantedPermissions] nvarchar(max) NOT NULL,
        [RevokedPermissions] nvarchar(max) NOT NULL,
        [LastActiveAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Staff] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [SupportTicketMessages] (
        [Id] nvarchar(450) NOT NULL,
        [TicketId] nvarchar(max) NOT NULL,
        [SenderId] nvarchar(max) NOT NULL,
        [SenderType] nvarchar(max) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [IsInternalNote] bit NOT NULL,
        [AttachmentsJson] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SupportTicketMessages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [SupportTickets] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(max) NOT NULL,
        [UserRole] nvarchar(max) NOT NULL,
        [Category] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [TripId] nvarchar(max) NULL,
        [Status] int NOT NULL,
        [Priority] int NOT NULL,
        [Reference] nvarchar(max) NOT NULL,
        [AssignedStaffId] nvarchar(max) NULL,
        [SlaDeadline] datetime2 NULL,
        [ResolutionCode] nvarchar(max) NULL,
        [Market] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SupportTickets] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [SurgeZones] (
        [Id] nvarchar(450) NOT NULL,
        [ZoneId] nvarchar(max) NOT NULL,
        [Multiplier] decimal(18,2) NOT NULL,
        [IsAutomatic] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [Reason] nvarchar(max) NULL,
        [ExpiresAt] datetime2 NULL,
        [OverriddenByStaffId] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SurgeZones] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Trips] (
        [Id] nvarchar(450) NOT NULL,
        [Code] nvarchar(max) NOT NULL,
        [Vertical] int NOT NULL,
        [ServiceClass] int NOT NULL,
        [RiderId] nvarchar(max) NOT NULL,
        [DriverId] nvarchar(max) NULL,
        [QuoteId] nvarchar(max) NULL,
        [Market] nvarchar(max) NOT NULL,
        [JobState] int NOT NULL,
        [PickupLat] decimal(18,2) NOT NULL,
        [PickupLng] decimal(18,2) NOT NULL,
        [PickupLabel] nvarchar(max) NOT NULL,
        [PickupPlaceId] nvarchar(max) NULL,
        [DropoffLat] decimal(18,2) NOT NULL,
        [DropoffLng] decimal(18,2) NOT NULL,
        [DropoffLabel] nvarchar(max) NOT NULL,
        [DropoffPlaceId] nvarchar(max) NULL,
        [EncodedPolyline] nvarchar(max) NULL,
        [DistanceM] int NULL,
        [DurationS] int NULL,
        [FareGross] bigint NOT NULL,
        [FareBase] bigint NOT NULL,
        [FareDistance] bigint NOT NULL,
        [FareTime] bigint NOT NULL,
        [FareWaiting] bigint NOT NULL,
        [FareServiceFee] bigint NOT NULL,
        [FareDiscount] bigint NOT NULL,
        [FareTip] bigint NOT NULL,
        [Currency] nvarchar(max) NOT NULL,
        [FareIsFinal] bit NOT NULL,
        [CommissionRate] decimal(18,2) NOT NULL,
        [CommissionAmount] bigint NOT NULL,
        [DriverEarnings] bigint NOT NULL,
        [CashCollected] bigint NOT NULL,
        [CashToRemit] bigint NOT NULL,
        [WalletCredit] bigint NOT NULL,
        [PaymentMethod] int NOT NULL,
        [PaymentId] nvarchar(max) NULL,
        [StartOtp] nvarchar(max) NULL,
        [ShareToken] nvarchar(max) NULL,
        [ShareTokenExpiresAt] datetime2 NULL,
        [NoteToDriver] nvarchar(max) NULL,
        [NoteAudioUrl] nvarchar(max) NULL,
        [RatingByRider] int NULL,
        [RatingByDriver] int NULL,
        [ScheduledAt] datetime2 NULL,
        [ForSomeoneElseName] nvarchar(max) NULL,
        [ForSomeoneElsePhone] nvarchar(max) NULL,
        [CancellationReasonCode] nvarchar(max) NULL,
        [CancellationFee] bigint NULL,
        [AssignedAt] datetime2 NULL,
        [ArrivedAt] datetime2 NULL,
        [StartedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        [CancelledAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Trips] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] nvarchar(450) NOT NULL,
        [FirstName] nvarchar(max) NOT NULL,
        [LastName] nvarchar(max) NOT NULL,
        [Phone] nvarchar(max) NOT NULL,
        [PhoneVerified] bit NOT NULL,
        [Email] nvarchar(max) NULL,
        [EmailVerified] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [Role] int NOT NULL,
        [Status] int NOT NULL,
        [PhotoUrl] nvarchar(max) NULL,
        [Language] nvarchar(max) NOT NULL,
        [SuspensionReason] nvarchar(max) NULL,
        [SuspendedUntil] datetime2 NULL,
        [ReferralCode] nvarchar(max) NULL,
        [ReferredByUserId] nvarchar(max) NULL,
        [MarketingOptIn] bit NOT NULL,
        [Rating] decimal(18,2) NOT NULL,
        [TotalRatings] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Zones] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Market] nvarchar(max) NOT NULL,
        [PolygonGeoJson] nvarchar(max) NOT NULL,
        [CenterLat] decimal(18,2) NOT NULL,
        [CenterLng] decimal(18,2) NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [VerticalsEnabled] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Zones] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [CoRideBookings] (
        [Id] nvarchar(450) NOT NULL,
        [ListingId] nvarchar(450) NOT NULL,
        [RiderId] nvarchar(max) NOT NULL,
        [Seats] int NOT NULL,
        [SeatLabelsJson] nvarchar(max) NOT NULL,
        [PricePerSeat] bigint NOT NULL,
        [ServiceFee] bigint NOT NULL,
        [PromoDiscount] bigint NOT NULL,
        [Total] bigint NOT NULL,
        [Currency] nvarchar(max) NOT NULL,
        [Status] int NOT NULL,
        [PaymentMethod] int NOT NULL,
        [PaymentId] nvarchar(max) NULL,
        [CancellationReason] nvarchar(max) NULL,
        [RatingByRider] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_CoRideBookings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CoRideBookings_CoRideListings_ListingId] FOREIGN KEY ([ListingId]) REFERENCES [CoRideListings] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [DriverWalletTransactions] (
        [Id] nvarchar(450) NOT NULL,
        [DriverWalletId] nvarchar(450) NOT NULL,
        [Type] nvarchar(max) NOT NULL,
        [Amount] bigint NOT NULL,
        [BalanceAfter] bigint NOT NULL,
        [Status] int NOT NULL,
        [ReferenceId] nvarchar(max) NULL,
        [Reason] nvarchar(max) NULL,
        [CreatedBy] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DriverWalletTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DriverWalletTransactions_DriverWallets_DriverWalletId] FOREIGN KEY ([DriverWalletId]) REFERENCES [DriverWallets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [WebhookEvents] (
        [Id] nvarchar(450) NOT NULL,
        [PaymentId] nvarchar(450) NOT NULL,
        [Gateway] nvarchar(max) NOT NULL,
        [EventId] nvarchar(max) NOT NULL,
        [EventType] nvarchar(max) NOT NULL,
        [RawPayload] nvarchar(max) NOT NULL,
        [IsProcessed] bit NOT NULL,
        [ProcessedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WebhookEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WebhookEvents_Payments_PaymentId] FOREIGN KEY ([PaymentId]) REFERENCES [Payments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [PayoutAttempts] (
        [Id] nvarchar(450) NOT NULL,
        [PayoutId] nvarchar(450) NOT NULL,
        [Status] int NOT NULL,
        [ProviderReference] nvarchar(max) NULL,
        [FailureReason] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PayoutAttempts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PayoutAttempts_Payouts_PayoutId] FOREIGN KEY ([PayoutId]) REFERENCES [Payouts] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [StaffRefreshTokens] (
        [Id] nvarchar(450) NOT NULL,
        [StaffId] nvarchar(450) NOT NULL,
        [Token] nvarchar(max) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [IsRevoked] bit NOT NULL,
        [DeviceInfo] nvarchar(max) NULL,
        [IpAddress] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StaffRefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StaffRefreshTokens_Staff_StaffId] FOREIGN KEY ([StaffId]) REFERENCES [Staff] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Conversations] (
        [Id] nvarchar(450) NOT NULL,
        [Kind] int NOT NULL,
        [TripId] nvarchar(450) NULL,
        [SupportTicketId] nvarchar(max) NULL,
        [IsClosed] bit NOT NULL,
        [ClosedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Conversations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Conversations_Trips_TripId] FOREIGN KEY ([TripId]) REFERENCES [Trips] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [SosIncidents] (
        [Id] nvarchar(450) NOT NULL,
        [TripId] nvarchar(450) NULL,
        [UserId] nvarchar(max) NOT NULL,
        [Source] nvarchar(max) NOT NULL,
        [Type] int NOT NULL,
        [Status] int NOT NULL,
        [Lat] decimal(18,2) NOT NULL,
        [Lng] decimal(18,2) NOT NULL,
        [AcknowledgedByStaffId] nvarchar(max) NULL,
        [AcknowledgedAt] datetime2 NULL,
        [Outcome] nvarchar(max) NULL,
        [Notes] nvarchar(max) NULL,
        [AuthorityName] nvarchar(max) NULL,
        [AuthorityReference] nvarchar(max) NULL,
        [Market] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SosIncidents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SosIncidents_Trips_TripId] FOREIGN KEY ([TripId]) REFERENCES [Trips] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [TripStateHistories] (
        [Id] nvarchar(450) NOT NULL,
        [TripId] nvarchar(450) NOT NULL,
        [State] int NOT NULL,
        [OccurredAt] datetime2 NOT NULL,
        [Actor] nvarchar(max) NOT NULL,
        [ActorId] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TripStateHistories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TripStateHistories_Trips_TripId] FOREIGN KEY ([TripId]) REFERENCES [Trips] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [BiometricTokens] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [DeviceId] nvarchar(max) NOT NULL,
        [Token] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_BiometricTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BiometricTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [DriverProfiles] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [KycStatus] int NOT NULL,
        [OnboardingComplete] bit NOT NULL,
        [IsOnline] bit NOT NULL,
        [VerticalsAllowed] nvarchar(max) NOT NULL,
        [LastLat] decimal(18,2) NULL,
        [LastLng] decimal(18,2) NULL,
        [LastHeading] decimal(18,2) NULL,
        [LastLocationAt] datetime2 NULL,
        [OnlineSecondsToday] int NOT NULL,
        [AcceptanceRate] decimal(18,2) NOT NULL,
        [CancellationRate] decimal(18,2) NOT NULL,
        [CompletionRate] decimal(18,2) NOT NULL,
        [TotalTripsCompleted] int NOT NULL,
        [BlockReason] nvarchar(max) NULL,
        [DriverWalletId] nvarchar(450) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DriverProfiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DriverProfiles_DriverWallets_DriverWalletId] FOREIGN KEY ([DriverWalletId]) REFERENCES [DriverWallets] ([Id]),
        CONSTRAINT [FK_DriverProfiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [EmergencyContacts] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Phone] nvarchar(max) NOT NULL,
        [Relationship] nvarchar(max) NULL,
        [NotifyOnTripStart] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_EmergencyContacts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmergencyContacts_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [Token] nvarchar(max) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [IsRevoked] bit NOT NULL,
        [DeviceId] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RefreshTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [SavedPlaces] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [Label] nvarchar(max) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Address] nvarchar(max) NOT NULL,
        [Lat] decimal(18,2) NOT NULL,
        [Lng] decimal(18,2) NOT NULL,
        [PlaceId] nvarchar(max) NULL,
        [Building] nvarchar(max) NULL,
        [Floor] nvarchar(max) NULL,
        [Instructions] nvarchar(max) NULL,
        [ContactPhone] nvarchar(max) NULL,
        [IsDefault] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SavedPlaces] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SavedPlaces_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [UserDevices] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [DeviceId] nvarchar(max) NOT NULL,
        [FcmToken] nvarchar(max) NULL,
        [Platform] nvarchar(max) NOT NULL,
        [AppVersion] nvarchar(max) NULL,
        [OsVersion] nvarchar(max) NULL,
        [Model] nvarchar(max) NULL,
        [Locale] nvarchar(max) NULL,
        [Timezone] nvarchar(max) NULL,
        [LastActiveAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_UserDevices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserDevices_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [UserPreferences] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [Key] nvarchar(max) NOT NULL,
        [Value] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_UserPreferences] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserPreferences_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Wallets] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [Balance] bigint NOT NULL,
        [PendingIn] bigint NOT NULL,
        [PendingOut] bigint NOT NULL,
        [Currency] nvarchar(max) NOT NULL,
        [IsLocked] bit NOT NULL,
        [LockReason] nvarchar(max) NULL,
        [MinTopup] bigint NOT NULL,
        [MaxBalance] bigint NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Wallets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Wallets_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Messages] (
        [Id] nvarchar(450) NOT NULL,
        [ConversationId] nvarchar(450) NOT NULL,
        [SenderId] nvarchar(max) NOT NULL,
        [SenderRole] nvarchar(max) NOT NULL,
        [Type] int NOT NULL,
        [Body] nvarchar(max) NULL,
        [MediaUrl] nvarchar(max) NULL,
        [DurationS] int NULL,
        [LocalId] nvarchar(max) NULL,
        [ReadAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Messages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Messages_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [DriverDocuments] (
        [Id] nvarchar(450) NOT NULL,
        [DriverId] nvarchar(max) NOT NULL,
        [Type] int NOT NULL,
        [FileUrl] nvarchar(max) NOT NULL,
        [BackFileUrl] nvarchar(max) NULL,
        [Status] int NOT NULL,
        [RejectionReason] nvarchar(max) NULL,
        [ExpiresAt] datetime2 NULL,
        [ReviewedByStaffId] nvarchar(max) NULL,
        [ReviewedAt] datetime2 NULL,
        [DriverProfileId] nvarchar(450) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DriverDocuments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DriverDocuments_DriverProfiles_DriverProfileId] FOREIGN KEY ([DriverProfileId]) REFERENCES [DriverProfiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [DriverOnboardings] (
        [Id] nvarchar(450) NOT NULL,
        [DriverId] nvarchar(450) NOT NULL,
        [PersonalStatus] int NOT NULL,
        [PersonalRejectionReason] nvarchar(max) NULL,
        [IdentityStatus] int NOT NULL,
        [IdentityRejectionReason] nvarchar(max) NULL,
        [LicenseStatus] int NOT NULL,
        [LicenseRejectionReason] nvarchar(max) NULL,
        [VehicleStatus] int NOT NULL,
        [VehicleRejectionReason] nvarchar(max) NULL,
        [InsuranceStatus] int NOT NULL,
        [InsuranceRejectionReason] nvarchar(max) NULL,
        [GuarantorStatus] int NOT NULL,
        [GuarantorRejectionReason] nvarchar(max) NULL,
        [PayoutStatus] int NOT NULL,
        [PayoutRejectionReason] nvarchar(max) NULL,
        [SelfieStatus] int NOT NULL,
        [SelfieRejectionReason] nvarchar(max) NULL,
        [PersonalDataJson] nvarchar(max) NULL,
        [IdentityDataJson] nvarchar(max) NULL,
        [LicenseDataJson] nvarchar(max) NULL,
        [VehicleDataJson] nvarchar(max) NULL,
        [InsuranceDataJson] nvarchar(max) NULL,
        [GuarantorDataJson] nvarchar(max) NULL,
        [PayoutDataJson] nvarchar(max) NULL,
        [SubmittedAt] datetime2 NULL,
        [ReviewedAt] datetime2 NULL,
        [AssignedReviewerStaffId] nvarchar(max) NULL,
        [FaceMatchScore] decimal(18,2) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DriverOnboardings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DriverOnboardings_DriverProfiles_DriverId] FOREIGN KEY ([DriverId]) REFERENCES [DriverProfiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [PayoutMethods] (
        [Id] nvarchar(450) NOT NULL,
        [DriverId] nvarchar(max) NOT NULL,
        [Method] nvarchar(max) NOT NULL,
        [BankCode] nvarchar(max) NULL,
        [AccountNumber] nvarchar(max) NULL,
        [AccountName] nvarchar(max) NULL,
        [Provider] nvarchar(max) NULL,
        [MobilePhone] nvarchar(max) NULL,
        [IsDefault] bit NOT NULL,
        [IsVerified] bit NOT NULL,
        [DriverProfileId] nvarchar(450) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PayoutMethods] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PayoutMethods_DriverProfiles_DriverProfileId] FOREIGN KEY ([DriverProfileId]) REFERENCES [DriverProfiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [Vehicles] (
        [Id] nvarchar(450) NOT NULL,
        [DriverId] nvarchar(max) NOT NULL,
        [Type] int NOT NULL,
        [Make] nvarchar(max) NOT NULL,
        [Model] nvarchar(max) NOT NULL,
        [Year] int NOT NULL,
        [Color] nvarchar(max) NOT NULL,
        [Plate] nvarchar(max) NOT NULL,
        [Seats] int NOT NULL,
        [IsActive] bit NOT NULL,
        [PendingReview] bit NOT NULL,
        [DriverProfileId] nvarchar(450) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Vehicles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Vehicles_DriverProfiles_DriverProfileId] FOREIGN KEY ([DriverProfileId]) REFERENCES [DriverProfiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE TABLE [WalletTransactions] (
        [Id] nvarchar(450) NOT NULL,
        [WalletId] nvarchar(450) NOT NULL,
        [Type] int NOT NULL,
        [Amount] bigint NOT NULL,
        [BalanceAfter] bigint NOT NULL,
        [Title] nvarchar(max) NOT NULL,
        [Subtitle] nvarchar(max) NULL,
        [ReferenceId] nvarchar(max) NULL,
        [Status] int NOT NULL,
        [Reason] nvarchar(max) NULL,
        [CreatedBy] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WalletTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WalletTransactions_Wallets_WalletId] FOREIGN KEY ([WalletId]) REFERENCES [Wallets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BiometricTokens_UserId] ON [BiometricTokens] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Conversations_TripId] ON [Conversations] ([TripId]) WHERE [TripId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CoRideBookings_ListingId] ON [CoRideBookings] ([ListingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DriverDocuments_DriverProfileId] ON [DriverDocuments] ([DriverProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DriverOnboardings_DriverId] ON [DriverOnboardings] ([DriverId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DriverProfiles_DriverWalletId] ON [DriverProfiles] ([DriverWalletId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DriverProfiles_UserId] ON [DriverProfiles] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DriverWalletTransactions_DriverWalletId] ON [DriverWalletTransactions] ([DriverWalletId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_EmergencyContacts_UserId] ON [EmergencyContacts] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Messages_ConversationId] ON [Messages] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PayoutAttempts_PayoutId] ON [PayoutAttempts] ([PayoutId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PayoutMethods_DriverProfileId] ON [PayoutMethods] ([DriverProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId] ON [RefreshTokens] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SavedPlaces_UserId] ON [SavedPlaces] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SosIncidents_TripId] ON [SosIncidents] ([TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StaffRefreshTokens_StaffId] ON [StaffRefreshTokens] ([StaffId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TripStateHistories_TripId] ON [TripStateHistories] ([TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserDevices_UserId] ON [UserDevices] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserPreferences_UserId] ON [UserPreferences] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Vehicles_DriverProfileId] ON [Vehicles] ([DriverProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Wallets_UserId] ON [Wallets] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WalletTransactions_WalletId] ON [WalletTransactions] ([WalletId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WebhookEvents_PaymentId] ON [WebhookEvents] ([PaymentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823141845_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823141845_InitialCreate', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823145353_AddUserProfileFields'
)
BEGIN
    ALTER TABLE [Users] ADD [DateOfBirth] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823145353_AddUserProfileFields'
)
BEGIN
    ALTER TABLE [Users] ADD [DeletionReason] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823145353_AddUserProfileFields'
)
BEGIN
    ALTER TABLE [Users] ADD [DeletionRequested] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823145353_AddUserProfileFields'
)
BEGIN
    ALTER TABLE [Users] ADD [DeletionRequestedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823145353_AddUserProfileFields'
)
BEGIN
    ALTER TABLE [Users] ADD [Gender] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823145353_AddUserProfileFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823145353_AddUserProfileFields', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823153708_FixDeviations'
)
BEGIN
    EXEC sp_rename N'[OtpRecords].[Phone]', N'Identifier', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823153708_FixDeviations'
)
BEGIN
    EXEC sp_rename N'[BiometricTokens].[Token]', N'TokenHash', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823153708_FixDeviations'
)
BEGIN
    ALTER TABLE [UserDevices] ADD [LastIpAddress] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823153708_FixDeviations'
)
BEGIN
    ALTER TABLE [UserDevices] ADD [LastLocation] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823153708_FixDeviations'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[IdempotencyRecords]') AND [c].[name] = N'UserId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [IdempotencyRecords] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [IdempotencyRecords] ALTER COLUMN [UserId] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823153708_FixDeviations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823153708_FixDeviations', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823160503_FixEntityConstructors'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823160503_FixEntityConstructors', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823164749_AddConfigAndBannerEntities'
)
BEGIN
    CREATE TABLE [AppPages] (
        [Id] nvarchar(450) NOT NULL,
        [Slug] nvarchar(max) NOT NULL,
        [Market] nvarchar(max) NOT NULL,
        [Language] nvarchar(max) NOT NULL,
        [ContentFormat] nvarchar(max) NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AppPages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823164749_AddConfigAndBannerEntities'
)
BEGIN
    CREATE TABLE [Banners] (
        [Id] nvarchar(450) NOT NULL,
        [ImageUrl] nvarchar(max) NOT NULL,
        [DeepLink] nvarchar(max) NULL,
        [Placement] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [Market] nvarchar(max) NOT NULL,
        [StartsAt] datetime2 NULL,
        [EndsAt] datetime2 NULL,
        [Order] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Banners] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823164749_AddConfigAndBannerEntities'
)
BEGIN
    CREATE TABLE [OnboardingSlides] (
        [Id] nvarchar(450) NOT NULL,
        [Audience] nvarchar(max) NOT NULL,
        [ImageUrl] nvarchar(max) NOT NULL,
        [Title] nvarchar(max) NOT NULL,
        [Subtitle] nvarchar(max) NULL,
        [Order] int NOT NULL,
        [IsActive] bit NOT NULL,
        [Market] nvarchar(max) NOT NULL,
        [Language] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_OnboardingSlides] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823164749_AddConfigAndBannerEntities'
)
BEGIN
    CREATE TABLE [PlatformConfigs] (
        [Id] nvarchar(450) NOT NULL,
        [Market] nvarchar(max) NOT NULL,
        [Currency] nvarchar(max) NOT NULL,
        [CurrencySymbol] nvarchar(max) NOT NULL,
        [Country] nvarchar(max) NOT NULL,
        [DefaultMapCenterLat] decimal(18,2) NOT NULL,
        [DefaultMapCenterLng] decimal(18,2) NOT NULL,
        [VerticalsEnabledJson] nvarchar(max) NOT NULL,
        [PaymentMethodsJson] nvarchar(max) NOT NULL,
        [SupportPhone] nvarchar(max) NOT NULL,
        [SosPhone] nvarchar(max) NOT NULL,
        [CancellationPolicyJson] nvarchar(max) NOT NULL,
        [ReferralEnabled] bit NOT NULL,
        [WalletEnabled] bit NOT NULL,
        [TippingEnabled] bit NOT NULL,
        [MinAppVersionIos] nvarchar(max) NOT NULL,
        [MinAppVersionAndroid] nvarchar(max) NOT NULL,
        [LatestAppVersionIos] nvarchar(max) NOT NULL,
        [LatestAppVersionAndroid] nvarchar(max) NOT NULL,
        [IosStoreUrl] nvarchar(max) NULL,
        [AndroidStoreUrl] nvarchar(max) NULL,
        [MaintenanceModeRider] bit NOT NULL,
        [MaintenanceModeDriver] bit NOT NULL,
        [MaintenanceMessage] nvarchar(max) NULL,
        [FeatureFlagsJson] nvarchar(max) NOT NULL,
        [OfferTimeoutSeconds] int NOT NULL,
        [SearchRadiusM] int NOT NULL,
        [DispatchStrategy] nvarchar(max) NOT NULL,
        [MaxConcurrentOffers] int NOT NULL,
        [LocationPingOnTripSeconds] int NOT NULL,
        [LocationPingIdleSeconds] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PlatformConfigs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823164749_AddConfigAndBannerEntities'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823164749_AddConfigAndBannerEntities', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823183540_AddBatch1Batch2Entities'
)
BEGIN
    CREATE TABLE [CancellationReasons] (
        [Id] nvarchar(450) NOT NULL,
        [Code] nvarchar(max) NOT NULL,
        [Label] nvarchar(max) NOT NULL,
        [Language] nvarchar(max) NOT NULL,
        [Audience] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [Order] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CancellationReasons] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823183540_AddBatch1Batch2Entities'
)
BEGIN
    CREATE TABLE [CoRideRequests] (
        [Id] nvarchar(450) NOT NULL,
        [RiderId] nvarchar(max) NOT NULL,
        [PickupLat] decimal(18,2) NOT NULL,
        [PickupLng] decimal(18,2) NOT NULL,
        [PickupLabel] nvarchar(max) NOT NULL,
        [DropoffLat] decimal(18,2) NOT NULL,
        [DropoffLng] decimal(18,2) NOT NULL,
        [DropoffLabel] nvarchar(max) NOT NULL,
        [SeatsNeeded] int NOT NULL,
        [DepartureWindowFrom] datetime2 NOT NULL,
        [DepartureWindowTo] datetime2 NOT NULL,
        [Status] int NOT NULL,
        [MatchedListingId] nvarchar(max) NULL,
        [MatchedAt] datetime2 NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CoRideRequests] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823183540_AddBatch1Batch2Entities'
)
BEGIN
    CREATE TABLE [DeviceDiagnostics] (
        [Id] nvarchar(450) NOT NULL,
        [DeviceId] nvarchar(max) NOT NULL,
        [UserId] nvarchar(max) NOT NULL,
        [BatteryLevel] int NULL,
        [LocationPermission] bit NULL,
        [BackgroundPermission] bit NULL,
        [MockLocationDetected] bit NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DeviceDiagnostics] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823183540_AddBatch1Batch2Entities'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823183540_AddBatch1Batch2Entities', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823185250_AddBatch3Entities'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823185250_AddBatch3Entities', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823220351_AddBatch4Money'
)
BEGIN
    CREATE TABLE [UserPaymentMethods] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(max) NOT NULL,
        [Type] nvarchar(max) NOT NULL,
        [Label] nvarchar(max) NOT NULL,
        [Last4] nvarchar(max) NULL,
        [Phone] nvarchar(max) NULL,
        [GatewayToken] nvarchar(max) NULL,
        [IsDefault] bit NOT NULL,
        [IsVerified] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_UserPaymentMethods] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823220351_AddBatch4Money'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823220351_AddBatch4Money', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823224823_AddBatch5Onboarding'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823224823_AddBatch5Onboarding', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823230020_AddBatch6CrossCutting'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823230020_AddBatch6CrossCutting', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823232700_FixBatch5Batch6Alignment'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823232700_FixBatch5Batch6Alignment', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824002439_AddBatchA1Admin'
)
BEGIN
    ALTER TABLE [StaffRefreshTokens] ADD [AbsoluteCreatedAt] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824002439_AddBatchA1Admin'
)
BEGIN
    ALTER TABLE [StaffRefreshTokens] ADD [SessionToken] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824002439_AddBatchA1Admin'
)
BEGIN
    CREATE TABLE [DispatchConfigs] (
        [Id] nvarchar(450) NOT NULL,
        [Market] nvarchar(max) NOT NULL,
        [OfferTimeoutSeconds] int NOT NULL,
        [SearchRadiusM] int NOT NULL,
        [Strategy] nvarchar(max) NOT NULL,
        [MaxConcurrentOffers] int NOT NULL,
        [LocationPingOnTripS] int NOT NULL,
        [LocationPingIdleS] int NOT NULL,
        [UpdatedByStaffId] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DispatchConfigs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824002439_AddBatchA1Admin'
)
BEGIN
    CREATE TABLE [FeatureFlags] (
        [Id] nvarchar(450) NOT NULL,
        [Key] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [Enabled] bit NOT NULL,
        [RolloutPct] int NOT NULL,
        [Scope] nvarchar(max) NOT NULL,
        [Market] nvarchar(max) NOT NULL,
        [UpdatedByStaffId] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_FeatureFlags] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824002439_AddBatchA1Admin'
)
BEGIN
    CREATE TABLE [Integrations] (
        [Id] nvarchar(450) NOT NULL,
        [Key] nvarchar(max) NOT NULL,
        [Label] nvarchar(max) NOT NULL,
        [IsConnected] bit NOT NULL,
        [LastError] nvarchar(max) NULL,
        [KeyLastRotatedAt] datetime2 NULL,
        [Market] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Integrations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824002439_AddBatchA1Admin'
)
BEGIN
    CREATE TABLE [StaffInvites] (
        [Id] nvarchar(450) NOT NULL,
        [TokenHash] nvarchar(max) NOT NULL,
        [Email] nvarchar(max) NOT NULL,
        [RoleKey] nvarchar(max) NOT NULL,
        [MarketsJson] nvarchar(max) NOT NULL,
        [InvitedByStaffId] nvarchar(max) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [IsUsed] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StaffInvites] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824002439_AddBatchA1Admin'
)
BEGIN
    CREATE TABLE [StaffPasswordResets] (
        [Id] nvarchar(450) NOT NULL,
        [TokenHash] nvarchar(max) NOT NULL,
        [StaffId] nvarchar(max) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [IsUsed] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StaffPasswordResets] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824002439_AddBatchA1Admin'
)
BEGIN
    CREATE TABLE [TwoFaChallenges] (
        [Id] nvarchar(450) NOT NULL,
        [StaffId] nvarchar(max) NOT NULL,
        [ChallengeTokenHash] nvarchar(max) NOT NULL,
        [AttemptCount] int NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [IsUsed] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TwoFaChallenges] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824002439_AddBatchA1Admin'
)
BEGIN
    CREATE TABLE [TwoFaRecoveryCodes] (
        [Id] nvarchar(450) NOT NULL,
        [StaffId] nvarchar(max) NOT NULL,
        [CodeHash] nvarchar(max) NOT NULL,
        [IsUsed] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TwoFaRecoveryCodes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824002439_AddBatchA1Admin'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824002439_AddBatchA1Admin', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824095926_AddBatchA3Entities'
)
BEGIN
    ALTER TABLE [Users] ADD [FlagReason] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824095926_AddBatchA3Entities'
)
BEGIN
    ALTER TABLE [Users] ADD [FlagSeverity] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824095926_AddBatchA3Entities'
)
BEGIN
    ALTER TABLE [Users] ADD [IsFlagged] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824095926_AddBatchA3Entities'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824095926_AddBatchA3Entities', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824110758_AddBatchA4Finance'
)
BEGIN
    ALTER TABLE [DriverWallets] ADD [IsLocked] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824110758_AddBatchA4Finance'
)
BEGIN
    ALTER TABLE [DriverWallets] ADD [LockReason] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824110758_AddBatchA4Finance'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824110758_AddBatchA4Finance', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824114733_AddBatchA5Growth'
)
BEGIN
    ALTER TABLE [PlatformConfigs] ADD [ReferralConfigJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824114733_AddBatchA5Growth'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824114733_AddBatchA5Growth', N'9.0.7');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824134624_A6_StaffRoleCustomizations'
)
BEGIN
    CREATE TABLE [StaffRoleCustomizations] (
        [Id] nvarchar(450) NOT NULL,
        [RoleKey] nvarchar(max) NOT NULL,
        [PermissionsJson] nvarchar(max) NOT NULL,
        [UpdatedByStaffId] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StaffRoleCustomizations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824134624_A6_StaffRoleCustomizations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824134624_A6_StaffRoleCustomizations', N'9.0.7');
END;

COMMIT;
GO


-- ============================================================
--  HANGFIRE TABLES
--  Created by Hangfire at runtime. Included here so the DB
--  can be fully recreated without needing to start the app.
-- ============================================================

IF OBJECT_ID(N'[Schema]') IS NULL
CREATE TABLE [Schema]
(
    [Version] INT NOT NULL,
    CONSTRAINT [PK_HangFire_Schema] PRIMARY KEY CLUSTERED ([Version])
);
GO

IF OBJECT_ID(N'[Server]') IS NULL
CREATE TABLE [Server]
(
    [Id]            NVARCHAR(200) NOT NULL,
    [Data]          NVARCHAR(MAX) NULL,
    [LastHeartbeat] DATETIME      NOT NULL,
    CONSTRAINT [PK_HangFire_Server] PRIMARY KEY CLUSTERED ([Id])
);
CREATE INDEX [IX_HangFire_Server_LastHeartbeat] ON [Server] ([LastHeartbeat]);
GO

IF OBJECT_ID(N'[Job]') IS NULL
BEGIN
    CREATE TABLE [Job]
    (
        [Id]             BIGINT        NOT NULL IDENTITY(1,1),
        [StateId]        BIGINT        NULL,
        [StateName]      NVARCHAR(20)  NULL,
        [InvocationData] NVARCHAR(MAX) NOT NULL,
        [Arguments]      NVARCHAR(MAX) NOT NULL,
        [CreatedAt]      DATETIME      NOT NULL,
        [ExpireAt]       DATETIME      NULL,
        CONSTRAINT [PK_HangFire_Job] PRIMARY KEY CLUSTERED ([Id])
    );
    CREATE NONCLUSTERED INDEX [IX_HangFire_Job_StateName] ON [Job] ([StateName]);
    CREATE NONCLUSTERED INDEX [IX_HangFire_Job_ExpireAt]  ON [Job] ([StateName], [ExpireAt]);
END;
GO

IF OBJECT_ID(N'[State]') IS NULL
BEGIN
    CREATE TABLE [State]
    (
        [Id]        BIGINT        NOT NULL IDENTITY(1,1),
        [JobId]     BIGINT        NOT NULL,
        [Name]      NVARCHAR(20)  NOT NULL,
        [Reason]    NVARCHAR(100) NULL,
        [CreatedAt] DATETIME      NOT NULL,
        [Data]      NVARCHAR(MAX) NULL,
        CONSTRAINT [PK_HangFire_State] PRIMARY KEY CLUSTERED ([JobId], [Id]),
        CONSTRAINT [FK_HangFire_State_Job] FOREIGN KEY ([JobId]) REFERENCES [Job]([Id]) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX [IX_HangFire_State_CreatedAt] ON [State] ([CreatedAt]);
END;
GO

IF OBJECT_ID(N'[JobParameter]') IS NULL
CREATE TABLE [JobParameter]
(
    [JobId] BIGINT        NOT NULL,
    [Name]  NVARCHAR(40)  NOT NULL,
    [Value] NVARCHAR(MAX) NULL,
    CONSTRAINT [PK_HangFire_JobParameter] PRIMARY KEY CLUSTERED ([JobId], [Name]),
    CONSTRAINT [FK_HangFire_JobParameter_Job] FOREIGN KEY ([JobId]) REFERENCES [Job]([Id]) ON DELETE CASCADE
);
GO

IF OBJECT_ID(N'[JobQueue]') IS NULL
CREATE TABLE [JobQueue]
(
    [Id]        BIGINT       NOT NULL IDENTITY(1,1),
    [JobId]     BIGINT       NOT NULL,
    [Queue]     NVARCHAR(50) NOT NULL,
    [FetchedAt] DATETIME     NULL,
    CONSTRAINT [PK_HangFire_JobQueue] PRIMARY KEY CLUSTERED ([Queue], [Id])
);
GO

IF OBJECT_ID(N'[Counter]') IS NULL
CREATE TABLE [Counter]
(
    [Id]       BIGINT       NOT NULL IDENTITY(1,1),
    [Key]      NVARCHAR(100) NOT NULL,
    [Value]    INT           NOT NULL,
    [ExpireAt] DATETIME      NULL,
    CONSTRAINT [PK_HangFire_Counter] PRIMARY KEY CLUSTERED ([Key], [Id])
);
GO

IF OBJECT_ID(N'[AggregatedCounter]') IS NULL
BEGIN
    CREATE TABLE [AggregatedCounter]
    (
        [Key]      NVARCHAR(100) NOT NULL,
        [Value]    BIGINT        NOT NULL,
        [ExpireAt] DATETIME      NULL,
        CONSTRAINT [PK_HangFire_CounterAggregated] PRIMARY KEY CLUSTERED ([Key])
    );
    CREATE NONCLUSTERED INDEX [IX_HangFire_AggregatedCounter_ExpireAt] ON [AggregatedCounter] ([ExpireAt]);
END;
GO

IF OBJECT_ID(N'[Hash]') IS NULL
BEGIN
    CREATE TABLE [Hash]
    (
        [Key]      NVARCHAR(100) NOT NULL,
        [Field]    NVARCHAR(100) NOT NULL,
        [Value]    NVARCHAR(MAX) NULL,
        [ExpireAt] DATETIME2     NULL,
        CONSTRAINT [PK_HangFire_Hash] PRIMARY KEY CLUSTERED ([Key], [Field])
    );
    CREATE NONCLUSTERED INDEX [IX_HangFire_Hash_ExpireAt] ON [Hash] ([ExpireAt]);
END;
GO

IF OBJECT_ID(N'[List]') IS NULL
BEGIN
    CREATE TABLE [List]
    (
        [Id]       BIGINT        NOT NULL IDENTITY(1,1),
        [Key]      NVARCHAR(100) NOT NULL,
        [Value]    NVARCHAR(MAX) NULL,
        [ExpireAt] DATETIME      NULL,
        CONSTRAINT [PK_HangFire_List] PRIMARY KEY CLUSTERED ([Key], [Id])
    );
    CREATE NONCLUSTERED INDEX [IX_HangFire_List_ExpireAt] ON [List] ([ExpireAt]);
END;
GO

IF OBJECT_ID(N'[Set]') IS NULL
BEGIN
    CREATE TABLE [Set]
    (
        [Key]      NVARCHAR(100) NOT NULL,
        [Score]    FLOAT         NOT NULL,
        [Value]    NVARCHAR(256) NOT NULL,
        [ExpireAt] DATETIME      NULL,
        CONSTRAINT [PK_HangFire_Set] PRIMARY KEY CLUSTERED ([Key], [Value])
    );
    CREATE NONCLUSTERED INDEX [IX_HangFire_Set_ExpireAt] ON [Set] ([ExpireAt]);
    CREATE NONCLUSTERED INDEX [IX_HangFire_Set_Score]    ON [Set] ([Key], [Score]);
END;
GO

-- ============================================================
--  PART 2 - SEED DATA
-- ============================================================
-- ============================================================
--  Izigo API â€“ Seed Data
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

-- â”€â”€ PlatformConfigs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
    'pc_ng', 'ng', 'NGN', N'â‚¦', 'NG',
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

-- â”€â”€ Staff â€” Super Admin â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

-- â”€â”€ FareRules â€” CI market only â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

-- â”€â”€ DispatchConfigs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

-- â”€â”€ CommissionConfigs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

-- â”€â”€ FeatureFlags (10 flags Ã— 2 markets = 20 rows) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

