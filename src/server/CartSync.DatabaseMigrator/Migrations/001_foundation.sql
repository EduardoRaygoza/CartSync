SET XACT_ABORT ON;

IF SCHEMA_ID(N'security') IS NULL EXEC(N'CREATE SCHEMA security AUTHORIZATION dbo');

IF OBJECT_ID(N'dbo.accounts', N'U') IS NULL
CREATE TABLE dbo.accounts
(
    id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_accounts PRIMARY KEY CLUSTERED,
    public_id uniqueidentifier NOT NULL CONSTRAINT uq_accounts_public_id UNIQUE,
    external_subject nvarchar(255) NOT NULL CONSTRAINT uq_accounts_external_subject UNIQUE,
    status nvarchar(32) NOT NULL CONSTRAINT ck_accounts_status CHECK (status IN (N'active', N'deletion_pending', N'deleted'))
);

IF OBJECT_ID(N'dbo.households', N'U') IS NULL
CREATE TABLE dbo.households
(
    id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_households PRIMARY KEY CLUSTERED,
    public_id uniqueidentifier NOT NULL CONSTRAINT uq_households_public_id UNIQUE,
    status nvarchar(32) NOT NULL CONSTRAINT ck_households_status CHECK (status IN (N'active', N'deletion_pending', N'deleted'))
);

IF OBJECT_ID(N'dbo.household_memberships', N'U') IS NULL
CREATE TABLE dbo.household_memberships
(
    id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_household_memberships PRIMARY KEY CLUSTERED,
    public_id uniqueidentifier NOT NULL CONSTRAINT uq_household_memberships_public_id UNIQUE,
    account_id bigint NOT NULL CONSTRAINT fk_memberships_account REFERENCES dbo.accounts(id),
    household_id bigint NOT NULL CONSTRAINT fk_memberships_household REFERENCES dbo.households(id),
    role nvarchar(16) NOT NULL CONSTRAINT ck_memberships_role CHECK (role IN (N'owner', N'member')),
    status nvarchar(32) NOT NULL CONSTRAINT ck_memberships_status CHECK (status IN (N'active', N'suspended', N'ended')),
    CONSTRAINT uq_memberships_account UNIQUE(account_id)
);

IF OBJECT_ID(N'dbo.household_cursors', N'U') IS NULL
CREATE TABLE dbo.household_cursors
(
    household_id uniqueidentifier NOT NULL CONSTRAINT pk_household_cursors PRIMARY KEY,
    [cursor] bigint NOT NULL CONSTRAINT df_household_cursors_cursor DEFAULT 0 CONSTRAINT ck_household_cursors_cursor CHECK ([cursor] >= 0)
);

IF DATABASE_PRINCIPAL_ID(N'cartsync_api') IS NULL CREATE ROLE cartsync_api AUTHORIZATION dbo;
GRANT SELECT ON dbo.accounts TO cartsync_api;
GRANT SELECT ON dbo.households TO cartsync_api;
GRANT SELECT ON dbo.household_memberships TO cartsync_api;
GRANT SELECT ON dbo.household_cursors TO cartsync_api;
-- CARTSYNC-BATCH
IF OBJECT_ID(N'security.household_access_predicate', N'IF') IS NULL
EXEC(N'CREATE FUNCTION security.household_access_predicate(@household_id uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN SELECT 1 AS allowed
WHERE @household_id = TRY_CONVERT(uniqueidentifier, SESSION_CONTEXT(N''household_id''));');

-- CARTSYNC-BATCH
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'household_cursors_policy')
EXEC(N'CREATE SECURITY POLICY security.household_cursors_policy
      ADD FILTER PREDICATE security.household_access_predicate(household_id) ON dbo.household_cursors
      WITH (STATE = ON)');
