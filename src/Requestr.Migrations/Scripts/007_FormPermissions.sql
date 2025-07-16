-- Form Permissions System Migration
-- This script adds granular role-based access control for forms
-- Migration: 007

USE RequestrApp;
GO

-- Create FormPermissions table for granular form-level permissions
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FormPermissions' AND xtype='U')
BEGIN
    CREATE TABLE FormPermissions (
        Id int IDENTITY(1,1) PRIMARY KEY,
        FormDefinitionId int NOT NULL,
        RoleName nvarchar(255) NOT NULL,
        PermissionType int NOT NULL, -- Enum values: CreateRequest=1, UpdateRequest=2, etc.
        IsGranted bit NOT NULL DEFAULT 1,
        Conditions nvarchar(max) NULL, -- JSON for future advanced conditions
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy nvarchar(255) NOT NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(255) NULL,
        
        FOREIGN KEY (FormDefinitionId) REFERENCES FormDefinitions(Id) ON DELETE CASCADE,
        INDEX IX_FormPermissions_FormDefinitionId (FormDefinitionId),
        INDEX IX_FormPermissions_RoleName (RoleName),
        INDEX IX_FormPermissions_PermissionType (PermissionType),
        UNIQUE (FormDefinitionId, RoleName, PermissionType) -- Prevent duplicate permissions
    );
    PRINT 'FormPermissions table created successfully.';
END
GO

-- Insert default permissions for existing forms
-- This gives Admin role full access to all existing forms
IF EXISTS (SELECT * FROM FormDefinitions) AND NOT EXISTS (SELECT * FROM FormPermissions)
BEGIN
    DECLARE @FormId int;
    DECLARE form_cursor CURSOR FOR 
        SELECT Id FROM FormDefinitions WHERE IsActive = 1;
    
    OPEN form_cursor;
    FETCH NEXT FROM form_cursor INTO @FormId;
    
    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Grant all permissions to Admin role for each form
        INSERT INTO FormPermissions (FormDefinitionId, RoleName, PermissionType, IsGranted, CreatedBy) VALUES
        (@FormId, 'Admin', 1, 1, 'System Migration'),  -- CreateRequest
        (@FormId, 'Admin', 2, 1, 'System Migration'),  -- UpdateRequest
        (@FormId, 'Admin', 3, 1, 'System Migration'),  -- DeleteRequest
        (@FormId, 'Admin', 10, 1, 'System Migration'), -- ViewData
        (@FormId, 'Admin', 11, 1, 'System Migration'), -- ViewDataDetails
        (@FormId, 'Admin', 20, 1, 'System Migration'), -- BulkActions
        (@FormId, 'Admin', 21, 1, 'System Migration'), -- BulkUploadCsv
        (@FormId, 'Admin', 22, 1, 'System Migration'), -- BulkEditRecords
        (@FormId, 'Admin', 23, 1, 'System Migration'), -- BulkDeleteRecords
        (@FormId, 'Admin', 30, 1, 'System Migration'), -- ViewAuditLog
        (@FormId, 'Admin', 31, 1, 'System Migration'), -- ExportData
        (@FormId, 'Admin', 32, 1, 'System Migration'); -- ManageFormSettings
        
        -- Grant basic permissions to FormAdmin role
        INSERT INTO FormPermissions (FormDefinitionId, RoleName, PermissionType, IsGranted, CreatedBy) VALUES
        (@FormId, 'FormAdmin', 1, 1, 'System Migration'),  -- CreateRequest
        (@FormId, 'FormAdmin', 2, 1, 'System Migration'),  -- UpdateRequest
        (@FormId, 'FormAdmin', 3, 1, 'System Migration'),  -- DeleteRequest
        (@FormId, 'FormAdmin', 10, 1, 'System Migration'), -- ViewData
        (@FormId, 'FormAdmin', 11, 1, 'System Migration'), -- ViewDataDetails
        (@FormId, 'FormAdmin', 20, 1, 'System Migration'), -- BulkActions
        (@FormId, 'FormAdmin', 21, 1, 'System Migration'), -- BulkUploadCsv
        (@FormId, 'FormAdmin', 22, 1, 'System Migration'), -- BulkEditRecords
        (@FormId, 'FormAdmin', 23, 1, 'System Migration'), -- BulkDeleteRecords
        (@FormId, 'FormAdmin', 30, 1, 'System Migration'), -- ViewAuditLog
        (@FormId, 'FormAdmin', 31, 1, 'System Migration'); -- ExportData
        
        -- Grant data management permissions to DataAdmin role
        INSERT INTO FormPermissions (FormDefinitionId, RoleName, PermissionType, IsGranted, CreatedBy) VALUES
        (@FormId, 'DataAdmin', 1, 1, 'System Migration'),  -- CreateRequest
        (@FormId, 'DataAdmin', 2, 1, 'System Migration'),  -- UpdateRequest
        (@FormId, 'DataAdmin', 3, 0, 'System Migration'),  -- DeleteRequest (disabled by default)
        (@FormId, 'DataAdmin', 10, 1, 'System Migration'), -- ViewData
        (@FormId, 'DataAdmin', 11, 1, 'System Migration'), -- ViewDataDetails
        (@FormId, 'DataAdmin', 20, 1, 'System Migration'), -- BulkActions
        (@FormId, 'DataAdmin', 21, 1, 'System Migration'), -- BulkUploadCsv
        (@FormId, 'DataAdmin', 22, 1, 'System Migration'), -- BulkEditRecords
        (@FormId, 'DataAdmin', 23, 0, 'System Migration'), -- BulkDeleteRecords (disabled by default)
        (@FormId, 'DataAdmin', 31, 1, 'System Migration'); -- ExportData
        
        FETCH NEXT FROM form_cursor INTO @FormId;
    END
    
    CLOSE form_cursor;
    DEALLOCATE form_cursor;
    
    PRINT 'Default form permissions inserted for existing forms.';
END
GO

PRINT 'Form permissions system migration completed successfully.';
GO
