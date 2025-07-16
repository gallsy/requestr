-- Bulk Request Support Migration
-- This script adds bulk request functionality to the Requestr application
-- Migration: 003

USE RequestrApp;
GO

-- Create BulkFormRequests table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='BulkFormRequests' AND xtype='U')
BEGIN
    CREATE TABLE BulkFormRequests (
        Id int IDENTITY(1,1) PRIMARY KEY,
        FormDefinitionId int NOT NULL,
        RequestType int NOT NULL, -- 0=Insert, 1=Update, 2=Delete
        FileName nvarchar(255) NOT NULL,
        TotalRows int NOT NULL,
        ValidRows int NOT NULL,
        InvalidRows int NOT NULL,
        SelectedRows int NOT NULL,
        Status int NOT NULL DEFAULT 0, -- 0=Pending, 1=Approved, 2=Rejected, 3=Applied, 4=Failed
        RequestedBy nvarchar(255) NOT NULL,
        RequestedByName nvarchar(255) NOT NULL,
        RequestedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        ApprovedBy nvarchar(255) NULL,
        ApprovedByName nvarchar(255) NULL,
        ApprovedAt datetime2 NULL,
        RejectionReason nvarchar(max) NULL,
        Comments nvarchar(max) NULL,
        ProcessingSummary nvarchar(max) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy nvarchar(255) NOT NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(255) NULL,
        FOREIGN KEY (FormDefinitionId) REFERENCES FormDefinitions(Id) ON DELETE CASCADE,
        INDEX IX_BulkFormRequests_FormDefinitionId (FormDefinitionId),
        INDEX IX_BulkFormRequests_RequestType (RequestType),
        INDEX IX_BulkFormRequests_Status (Status),
        INDEX IX_BulkFormRequests_RequestedBy (RequestedBy),
        INDEX IX_BulkFormRequests_RequestedAt (RequestedAt)
    );
    PRINT 'BulkFormRequests table created successfully.';
END
GO

-- Create BulkFormRequestHistory table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='BulkFormRequestHistory' AND xtype='U')
BEGIN
    CREATE TABLE BulkFormRequestHistory (
        Id int IDENTITY(1,1) PRIMARY KEY,
        BulkFormRequestId int NOT NULL,
        ChangeType int NOT NULL, -- Same as FormRequestChangeType enum
        ChangedBy nvarchar(255) NOT NULL,
        ChangedByName nvarchar(255) NOT NULL,
        ChangedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        Comments nvarchar(max) NULL,
        Details nvarchar(max) NULL,
        FOREIGN KEY (BulkFormRequestId) REFERENCES BulkFormRequests(Id) ON DELETE CASCADE,
        INDEX IX_BulkFormRequestHistory_BulkFormRequestId (BulkFormRequestId),
        INDEX IX_BulkFormRequestHistory_ChangedAt (ChangedAt)
    );
    PRINT 'BulkFormRequestHistory table created successfully.';
END
GO

-- Create BulkFormRequestItems table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='BulkFormRequestItems' AND xtype='U')
BEGIN
    CREATE TABLE BulkFormRequestItems (
        Id int IDENTITY(1,1) PRIMARY KEY,
        BulkFormRequestId int NOT NULL,
        FieldValues nvarchar(max) NOT NULL, -- JSON
        OriginalValues nvarchar(max) NULL, -- JSON for updates
        RowNumber int NOT NULL,
        Status int NOT NULL DEFAULT 0, -- 0=Pending, 1=Approved, 2=Rejected, 3=Applied, 4=Failed
        ValidationErrors nvarchar(max) NULL, -- JSON array
        ProcessingResult nvarchar(max) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy nvarchar(255) NOT NULL DEFAULT 'System',
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(255) NULL,
        FOREIGN KEY (BulkFormRequestId) REFERENCES BulkFormRequests(Id) ON DELETE CASCADE,
        INDEX IX_BulkFormRequestItems_BulkFormRequestId (BulkFormRequestId),
        INDEX IX_BulkFormRequestItems_Status (Status),
        INDEX IX_BulkFormRequestItems_RowNumber (RowNumber)
    );
    PRINT 'BulkFormRequestItems table created successfully.';
END
GO

-- Add BulkFormRequestId column to FormRequests table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormRequests') AND name = 'BulkFormRequestId')
BEGIN
    ALTER TABLE FormRequests
    ADD BulkFormRequestId int NULL;
    
    -- Add foreign key constraint
    ALTER TABLE FormRequests
    ADD CONSTRAINT FK_FormRequests_BulkFormRequests
    FOREIGN KEY (BulkFormRequestId) REFERENCES BulkFormRequests(Id);
    
    -- Create index for better performance
    CREATE INDEX IX_FormRequests_BulkFormRequestId ON FormRequests(BulkFormRequestId);
    
    PRINT 'BulkFormRequestId column added to FormRequests table';
END
GO

PRINT 'Bulk request migration completed successfully.';
GO
