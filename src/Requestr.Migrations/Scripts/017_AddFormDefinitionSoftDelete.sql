-- Add soft delete columns to FormDefinitions table
-- Migration: 017

USE Requestr;
GO

-- Add IsDeleted column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'IsDeleted')
BEGIN
    ALTER TABLE FormDefinitions ADD IsDeleted bit NOT NULL DEFAULT 0;
    PRINT 'Added IsDeleted column to FormDefinitions table.';
END
GO

-- Add DeletedAt column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'DeletedAt')
BEGIN
    ALTER TABLE FormDefinitions ADD DeletedAt datetime2 NULL;
    PRINT 'Added DeletedAt column to FormDefinitions table.';
END
GO

-- Add DeletedBy column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'DeletedBy')
BEGIN
    ALTER TABLE FormDefinitions ADD DeletedBy nvarchar(255) NULL;
    PRINT 'Added DeletedBy column to FormDefinitions table.';
END
GO

-- Add index for soft delete filtering
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FormDefinitions_IsDeleted' AND object_id = OBJECT_ID('FormDefinitions'))
BEGIN
    CREATE INDEX IX_FormDefinitions_IsDeleted ON FormDefinitions(IsDeleted);
    PRINT 'Added IX_FormDefinitions_IsDeleted index.';
END
GO

PRINT 'Migration 017_AddFormDefinitionSoftDelete completed successfully.';
GO
