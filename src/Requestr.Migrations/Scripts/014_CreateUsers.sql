-- Create Users table for centralized identity
-- No data backfill required; lazy upsert from Entra claims at runtime

USE Requestr;
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Users]
    (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Users] PRIMARY KEY,
        [UserObjectId] UNIQUEIDENTIFIER NOT NULL, -- Entra OID
        [TenantId] UNIQUEIDENTIFIER NULL,         -- Entra Tenant ID (nullable for guests/unknown)
        [DisplayName] NVARCHAR(255) NULL,
        [Email] NVARCHAR(256) NULL,
        [UPN] NVARCHAR(255) NULL,
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_Users_CreatedAt] DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] DATETIME2 NULL,
        [LastSeenAt] DATETIME2 NULL
    );

    -- Ensure uniqueness per tenant; allow NULL tenant (treated as a distinct bucket)
    CREATE UNIQUE INDEX [UX_Users_UserObjectId_TenantId]
        ON [dbo].[Users] ([UserObjectId], [TenantId]);

    -- Helpful lookups
    CREATE INDEX [IX_Users_Email] ON [dbo].[Users]([Email]);
END
GO

PRINT 'Users table migration completed successfully.';
GO
