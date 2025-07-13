-- Migration: Add WorkflowFormRequestId to BulkFormRequests table
-- This allows bulk requests to reference the temp FormRequest created for workflow

USE [RequestrApp]
GO

PRINT 'Adding WorkflowFormRequestId column to BulkFormRequests table...'

-- Add WorkflowFormRequestId column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('BulkFormRequests') AND name = 'WorkflowFormRequestId')
BEGIN
    ALTER TABLE BulkFormRequests
    ADD WorkflowFormRequestId int NULL;
    
    PRINT 'WorkflowFormRequestId column added to BulkFormRequests table.';
END
ELSE
BEGIN
    PRINT 'WorkflowFormRequestId column already exists in BulkFormRequests table.';
END
GO

-- Update existing bulk requests to populate WorkflowFormRequestId (only if column exists)
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('BulkFormRequests') AND name = 'WorkflowFormRequestId')
BEGIN
    UPDATE bfr
    SET WorkflowFormRequestId = fr.Id
    FROM BulkFormRequests bfr
    INNER JOIN FormRequests fr ON fr.BulkFormRequestId = bfr.Id AND fr.WorkflowInstanceId = bfr.WorkflowInstanceId
    WHERE bfr.WorkflowInstanceId IS NOT NULL 
    AND bfr.WorkflowFormRequestId IS NULL;

    PRINT 'Updated existing bulk requests with WorkflowFormRequestId where applicable.';
END
ELSE
BEGIN
    PRINT 'WorkflowFormRequestId column does not exist, skipping data update.';
END

PRINT 'Migration completed successfully.'
GO
