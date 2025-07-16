-- Migration: Add Email Test Mode Support
-- Description: Adds Mode column to EmailConfiguration table to support test mode functionality
-- Date: 2025-07-16
-- Version: 010

-- Add Mode column to EmailConfiguration table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[EmailConfiguration]') AND name = 'Mode')
BEGIN
    ALTER TABLE [dbo].[EmailConfiguration]
    ADD [Mode] INT NOT NULL DEFAULT 0; -- 0 = Production, 1 = Test
    
    PRINT 'Added Mode column to EmailConfiguration table with default value 0 (Production mode)';
END
ELSE
BEGIN
    PRINT 'Mode column already exists in EmailConfiguration table';
END

PRINT 'EmailConfiguration table migration completed successfully';
