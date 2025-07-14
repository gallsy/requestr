-- Remove old ApplicationPermissions table and migrate to new FormPermissions system
-- This script removes the legacy global permission system in favor of the new form-based permission system

USE RequestrApp;
GO

PRINT 'Starting cleanup of old permission system...';
GO

-- Drop the old ApplicationPermissions table
IF EXISTS (SELECT * FROM sysobjects WHERE name='ApplicationPermissions' AND xtype='U')
BEGIN
    DROP TABLE ApplicationPermissions;
    PRINT 'ApplicationPermissions table dropped successfully.';
END
ELSE
BEGIN
    PRINT 'ApplicationPermissions table does not exist, skipping.';
END
GO

PRINT 'Old permission system cleanup completed successfully!';
GO

-- Note: The new FormPermissions table provides superior functionality:
-- - Type-safe permissions using enums instead of strings
-- - Per-form granular control instead of global permissions
-- - Dynamic role names defined by administrators
-- - Comprehensive UI integration in FormBuilder
-- - Better security model with resource-based authorization
