SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

-- The reducer transaction acquires sys.sp_getapplock with Transaction ownership
-- before advancing the per-Household cursor in this schema.

CREATE TABLE dbo.HouseholdCursor (
    HouseholdId uniqueidentifier NOT NULL PRIMARY KEY,
    NextCursor bigint NOT NULL CONSTRAINT DF_HouseholdCursor_Next DEFAULT 0
);

CREATE TABLE dbo.OperationReceipt (
    HouseholdId uniqueidentifier NOT NULL,
    OperationId uniqueidentifier NOT NULL,
    MemberId uniqueidentifier NOT NULL,
    DeviceSequence bigint NOT NULL,
    Outcome varchar(24) NOT NULL,
    HouseholdCursor bigint NOT NULL,
    Metadata nvarchar(1000) NULL,
    AppliedAt datetime2 NOT NULL CONSTRAINT DF_OperationReceipt_AppliedAt DEFAULT SYSUTCDATETIME(),
    ExpiresAt AS DATEADD(day, 180, AppliedAt) PERSISTED,
    CONSTRAINT PK_OperationReceipt PRIMARY KEY CLUSTERED (HouseholdId, OperationId)
);

CREATE TABLE dbo.Product (
    ProductKey bigint IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
    ProductId uniqueidentifier NOT NULL,
    HouseholdId uniqueidentifier NOT NULL,
    Name nvarchar(200) NOT NULL,
    NormalizedName nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Note nvarchar(1000) NOT NULL CONSTRAINT DF_Product_Note DEFAULT N'',
    IsArchived bit NOT NULL CONSTRAINT DF_Product_Archived DEFAULT 0,
    CONSTRAINT UQ_Product_PublicId UNIQUE (ProductId),
    CONSTRAINT UQ_Product_Normalized UNIQUE (HouseholdId, NormalizedName)
);

CREATE TABLE dbo.Trip (
    TripKey bigint IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
    TripId uniqueidentifier NOT NULL,
    HouseholdId uniqueidentifier NOT NULL,
    StoreId uniqueidentifier NOT NULL,
    Name nvarchar(200) NOT NULL,
    CompletedAt datetime2 NULL,
    CompletionCursor bigint NULL,
    CONSTRAINT UQ_Trip_PublicId UNIQUE (TripId)
);

CREATE TABLE dbo.TripEntry (
    TripEntryKey bigint IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
    TripEntryId uniqueidentifier NOT NULL,
    HouseholdId uniqueidentifier NOT NULL,
    TripKey bigint NOT NULL REFERENCES dbo.Trip(TripKey),
    ProductKey bigint NOT NULL REFERENCES dbo.Product(ProductKey),
    Amount decimal(18,3) NOT NULL,
    Unit varchar(12) NOT NULL,
    DepartmentId uniqueidentifier NULL,
    IsAcquired bit NOT NULL CONSTRAINT DF_TripEntry_Acquired DEFAULT 0,
    IsRemoved bit NOT NULL CONSTRAINT DF_TripEntry_Removed DEFAULT 0,
    CONSTRAINT CK_TripEntry_Amount CHECK (Amount > 0),
    CONSTRAINT UQ_TripEntry_PublicId UNIQUE (TripEntryId),
    CONSTRAINT UQ_TripEntry_Product UNIQUE (TripKey, ProductKey)
);

CREATE TABLE dbo.FieldVersion (
    HouseholdId uniqueidentifier NOT NULL,
    EntityType varchar(32) NOT NULL,
    EntityId uniqueidentifier NOT NULL,
    FieldName varchar(64) NOT NULL,
    BaseCursor bigint NOT NULL,
    InstallationId uniqueidentifier NOT NULL,
    DeviceSequence bigint NOT NULL,
    OperationId uniqueidentifier NOT NULL,
    CONSTRAINT PK_FieldVersion PRIMARY KEY CLUSTERED (HouseholdId, EntityType, EntityId, FieldName)
);

CREATE TABLE dbo.IdentityAlias (
    HouseholdId uniqueidentifier NOT NULL,
    AliasId uniqueidentifier NOT NULL,
    CanonicalId uniqueidentifier NOT NULL,
    EntityType varchar(32) NOT NULL,
    CONSTRAINT PK_IdentityAlias PRIMARY KEY CLUSTERED (HouseholdId, AliasId)
);

CREATE TABLE dbo.Tombstone (
    HouseholdId uniqueidentifier NOT NULL,
    EntityType varchar(32) NOT NULL,
    EntityId uniqueidentifier NOT NULL,
    RemovedCursor bigint NOT NULL,
    CONSTRAINT PK_Tombstone PRIMARY KEY CLUSTERED (HouseholdId, EntityType, EntityId)
);

CREATE TABLE dbo.SyncIssue (
    SyncIssueKey bigint IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
    SyncIssueId uniqueidentifier NOT NULL UNIQUE,
    HouseholdId uniqueidentifier NOT NULL,
    MemberId uniqueidentifier NOT NULL,
    Reason varchar(64) NOT NULL,
    OriginalOperation nvarchar(max) NOT NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_SyncIssue_Created DEFAULT SYSUTCDATETIME(),
    ResolvedAt datetime2 NULL
);

GO
CREATE FUNCTION dbo.fn_household_access(@HouseholdId uniqueidentifier)
RETURNS TABLE WITH SCHEMABINDING AS
RETURN SELECT 1 AS allowed
WHERE @HouseholdId = TRY_CONVERT(uniqueidentifier, SESSION_CONTEXT(N'household_id'));
GO
CREATE SECURITY POLICY dbo.HouseholdIsolation
ADD FILTER PREDICATE dbo.fn_household_access(HouseholdId) ON dbo.Product,
ADD BLOCK PREDICATE dbo.fn_household_access(HouseholdId) ON dbo.Product AFTER INSERT
WITH (STATE = ON);
GO
