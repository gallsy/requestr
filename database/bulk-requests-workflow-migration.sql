-- Migration: Add workflow support to BulkFormRequests
-- This script adds WorkflowInstanceId column to support workflow-based approvals for bulk requests

USE [RequestrApp]
GO

PRINT 'Starting BulkFormRequests workflow migration...'

-- Add WorkflowInstanceId column to BulkFormRequests table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[BulkFormRequests]') AND name = 'WorkflowInstanceId')
BEGIN
    ALTER TABLE BulkFormRequests
    ADD WorkflowInstanceId int NULL;
    
    -- Add foreign key constraint
    ALTER TABLE BulkFormRequests
    ADD CONSTRAINT FK_BulkFormRequests_WorkflowInstances 
    FOREIGN KEY (WorkflowInstanceId) REFERENCES WorkflowInstances(Id);
    
    -- Add index for better performance
    CREATE INDEX IX_BulkFormRequests_WorkflowInstanceId ON BulkFormRequests(WorkflowInstanceId);
    
    PRINT 'WorkflowInstanceId column added to BulkFormRequests table.';
END
ELSE
BEGIN
    PRINT 'WorkflowInstanceId column already exists in BulkFormRequests table.';
END

PRINT 'BulkFormRequests workflow migration completed successfully.'
GO
