-- Migration: Add TreatBlankAsNull flag to FormFields
-- Description: Enables per-field behavior to persist NULL when user submits a blank value
-- Date: 2025-09-11
-- Version: 016

USE Requestr;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'dbo.FormFields') 
      AND name = 'TreatBlankAsNull')
BEGIN
    ALTER TABLE dbo.FormFields ADD TreatBlankAsNull BIT NOT NULL CONSTRAINT DF_FormFields_TreatBlankAsNull DEFAULT(0);
    PRINT 'Added TreatBlankAsNull column to FormFields (default 0).';
END
ELSE
BEGIN
    PRINT 'TreatBlankAsNull column already exists on FormFields.';
END
GO

-- Safety: ensure no NULL values (should not occur due to NOT NULL + default)
UPDATE dbo.FormFields SET TreatBlankAsNull = 0 WHERE TreatBlankAsNull IS NULL;
GO

PRINT '016_AddTreatBlankAsNull migration completed.';
GO
