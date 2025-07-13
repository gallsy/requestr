-- Migration: Create BulkFormRequestItems table and update bulk request workflow
-- This script creates a new table for bulk request items and removes dependency on FormRequests

USE [RequestrApp]
GO

PRINT 'Starting bulk request items migration...'

-- Create BulkFormRequestItems table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BulkFormRequestItems')
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
ELSE
BEGIN
    PRINT 'BulkFormRequestItems table already exists.';
END

-- Note: We're not migrating existing data automatically since the structure is changing
-- Existing FormRequests linked to bulk requests will remain for backward compatibility
-- but new bulk requests will use BulkFormRequestItems

PRINT 'Bulk request items migration completed successfully.'
GO
