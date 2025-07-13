-- Requestr Application Database Schema
-- This script creates the complete schema for the Requestr forms application
-- Consolidated from multiple migration scripts into a single comprehensive setup

-- Create the database if it doesn't exist
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'RequestrApp')
BEGIN
    CREATE DATABASE RequestrApp;
END
GO

USE RequestrApp;
GO

-- FormDefinitions table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FormDefinitions' AND xtype='U')
BEGIN
    CREATE TABLE FormDefinitions (
        Id int IDENTITY(1,1) PRIMARY KEY,
        Name nvarchar(255) NOT NULL,
        Description nvarchar(max) NULL,
        DatabaseConnectionName nvarchar(255) NOT NULL,
        TableName nvarchar(255) NOT NULL,
        [Schema] nvarchar(255) NOT NULL DEFAULT 'dbo',
        ApproverRoles nvarchar(max) NULL, -- JSON array of roles
        RequiresApproval bit NOT NULL DEFAULT 1,
        IsActive bit NOT NULL DEFAULT 1,
        Category nvarchar(255) NULL, -- Added from add-category-field.sql
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy nvarchar(255) NOT NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(255) NULL,
        INDEX IX_FormDefinitions_IsActive (IsActive),
        INDEX IX_FormDefinitions_DatabaseConnectionName (DatabaseConnectionName),
        INDEX IX_FormDefinitions_TableName (TableName),
        INDEX IX_FormDefinitions_Category (Category) -- Added from add-category-field.sql
    );
    PRINT 'FormDefinitions table created successfully.';
END
ELSE
BEGIN
    -- Check if the Category column exists and add it if it doesn't
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'Category')
    BEGIN
        ALTER TABLE FormDefinitions
        ADD Category nvarchar(255) NULL;
        
        -- Create an index for better performance when querying by category
        CREATE INDEX IX_FormDefinitions_Category ON FormDefinitions(Category);
        
        PRINT 'Category column added to FormDefinitions table';
    END
END
GO

-- FormFields table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FormFields' AND xtype='U')
BEGIN
    CREATE TABLE FormFields (
        Id int IDENTITY(1,1) PRIMARY KEY,
        FormDefinitionId int NOT NULL,
        Name nvarchar(255) NOT NULL,
        DisplayName nvarchar(255) NOT NULL,
        DataType nvarchar(50) NOT NULL,
        MaxLength int NOT NULL DEFAULT 0,
        IsRequired bit NOT NULL DEFAULT 0,
        IsReadOnly bit NOT NULL DEFAULT 0,
        IsVisible bit NOT NULL DEFAULT 1,
        IsVisibleInDataView bit NOT NULL DEFAULT 1,
        DefaultValue nvarchar(max) NULL,
        ValidationRegex nvarchar(500) NULL,
        ValidationMessage nvarchar(500) NULL,
        VisibilityCondition nvarchar(500) NULL,
        ControlType nvarchar(50) NULL, -- input, textarea, checkbox, select, date, datetime, time
        DropdownOptions nvarchar(max) NULL, -- JSON array of options for select controls
        DisplayOrder int NOT NULL DEFAULT 0,
        FOREIGN KEY (FormDefinitionId) REFERENCES FormDefinitions(Id) ON DELETE CASCADE,
        INDEX IX_FormFields_FormDefinitionId (FormDefinitionId),
        INDEX IX_FormFields_DisplayOrder (DisplayOrder)
    );
    PRINT 'FormFields table created successfully.';
END
ELSE
BEGIN
    -- Check if the ControlType column exists and add it if it doesn't
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormFields') AND name = 'ControlType')
    BEGIN
        ALTER TABLE FormFields
        ADD ControlType nvarchar(50) NULL;
        
        PRINT 'ControlType column added to FormFields table';
    END
    
    -- Check if the DropdownOptions column exists and add it if it doesn't
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormFields') AND name = 'DropdownOptions')
    BEGIN
        ALTER TABLE FormFields
        ADD DropdownOptions nvarchar(max) NULL;
        
        PRINT 'DropdownOptions column added to FormFields table';
    END
    
    -- Check if the IsVisibleInDataView column exists and add it if it doesn't
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormFields') AND name = 'IsVisibleInDataView')
    BEGIN
        ALTER TABLE FormFields
        ADD IsVisibleInDataView bit NOT NULL DEFAULT 1;
        
        PRINT 'IsVisibleInDataView column added to FormFields table';
    END
END
GO

-- FormRequests table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FormRequests' AND xtype='U')
BEGIN
    CREATE TABLE FormRequests (
        Id int IDENTITY(1,1) PRIMARY KEY,
        FormDefinitionId int NOT NULL,
        RequestType int NOT NULL, -- 0=Insert, 1=Update, 2=Delete
        FieldValues nvarchar(max) NOT NULL, -- JSON
        OriginalValues nvarchar(max) NULL, -- JSON for UPDATE/DELETE operations
        Status int NOT NULL DEFAULT 0, -- 0=Pending, 1=Approved, 2=Rejected, 3=Applied, 4=Failed
        RequestedBy nvarchar(255) NOT NULL,
        RequestedByName nvarchar(255) NOT NULL,
        RequestedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        ApprovedBy nvarchar(255) NULL,
        ApprovedByName nvarchar(255) NULL,
        ApprovedAt datetime2 NULL,
        RejectionReason nvarchar(max) NULL,
        Comments nvarchar(max) NULL,
        AppliedRecordKey nvarchar(255) NULL, -- Added from add-applied-record-key.sql
        FailureMessage nvarchar(max) NULL,   -- Added from add-applied-record-key.sql
        FOREIGN KEY (FormDefinitionId) REFERENCES FormDefinitions(Id),
        INDEX IX_FormRequests_Status (Status),
        INDEX IX_FormRequests_RequestType (RequestType),
        INDEX IX_FormRequests_RequestedBy (RequestedBy),
        INDEX IX_FormRequests_FormDefinitionId (FormDefinitionId),
        INDEX IX_FormRequests_RequestedAt (RequestedAt),
        INDEX IX_FormRequests_AppliedRecordKey (AppliedRecordKey) -- Added from add-applied-record-key.sql
    );
    PRINT 'FormRequests table created successfully.';
END
ELSE
BEGIN
    -- Add AppliedRecordKey column if it doesn't exist
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS 
                  WHERE TABLE_NAME = 'FormRequests' 
                  AND COLUMN_NAME = 'AppliedRecordKey')
    BEGIN
        ALTER TABLE FormRequests
        ADD AppliedRecordKey nvarchar(255) NULL;
        
        PRINT 'Added AppliedRecordKey column to FormRequests table';
    END

    -- Add FailureMessage column if it doesn't exist
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS 
                  WHERE TABLE_NAME = 'FormRequests' 
                  AND COLUMN_NAME = 'FailureMessage')
    BEGIN
        ALTER TABLE FormRequests
        ADD FailureMessage nvarchar(max) NULL;
        
        PRINT 'Added FailureMessage column to FormRequests table';
    END

    -- Add index for AppliedRecordKey for performance
    IF NOT EXISTS (SELECT * FROM sys.indexes 
                  WHERE name = 'IX_FormRequests_AppliedRecordKey' 
                  AND object_id = OBJECT_ID('FormRequests'))
    BEGIN
        CREATE INDEX IX_FormRequests_AppliedRecordKey ON FormRequests(AppliedRecordKey);
        PRINT 'Created index IX_FormRequests_AppliedRecordKey';
    END
END
GO

-- FormRequestHistory table for change tracking
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FormRequestHistory' AND xtype='U')
BEGIN
    CREATE TABLE FormRequestHistory (
        Id int IDENTITY(1,1) PRIMARY KEY,
        FormRequestId int NOT NULL,
        ChangeType int NOT NULL, -- 0=Created, 1=Updated, 2=StatusChanged, 3=Approved, 4=Rejected, 5=Applied, 6=Failed
        PreviousValues nvarchar(max) NULL, -- JSON of previous values
        NewValues nvarchar(max) NULL, -- JSON of new values
        ChangedBy nvarchar(255) NOT NULL,
        ChangedByName nvarchar(255) NOT NULL,
        ChangedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        Comments nvarchar(max) NULL,
        FOREIGN KEY (FormRequestId) REFERENCES FormRequests(Id) ON DELETE CASCADE,
        INDEX IX_FormRequestHistory_FormRequestId (FormRequestId),
        INDEX IX_FormRequestHistory_ChangedAt (ChangedAt),
        INDEX IX_FormRequestHistory_ChangeType (ChangeType)
    );
    PRINT 'FormRequestHistory table created successfully.';
END
GO

PRINT 'Requestr application database schema created successfully!'
GO
