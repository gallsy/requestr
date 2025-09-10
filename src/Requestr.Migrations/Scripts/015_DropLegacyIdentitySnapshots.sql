-- Drop legacy snapshot identity columns (no back-compat required)
USE Requestr;
GO

-- Helper: drop column if it exists
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.FormRequests') AND name = 'RequestedByName')
BEGIN
    ALTER TABLE dbo.FormRequests DROP COLUMN RequestedByName;
    PRINT 'Dropped FormRequests.RequestedByName';
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.FormRequests') AND name = 'ApprovedByName')
BEGIN
    ALTER TABLE dbo.FormRequests DROP COLUMN ApprovedByName;
    PRINT 'Dropped FormRequests.ApprovedByName';
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.FormRequestHistory') AND name = 'ChangedByName')
BEGIN
    ALTER TABLE dbo.FormRequestHistory DROP COLUMN ChangedByName;
    PRINT 'Dropped FormRequestHistory.ChangedByName';
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WorkflowStepInstances') AND name = 'CompletedByName')
BEGIN
    ALTER TABLE dbo.WorkflowStepInstances DROP COLUMN CompletedByName;
    PRINT 'Dropped WorkflowStepInstances.CompletedByName';
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.BulkFormRequests') AND name = 'RequestedByName')
BEGIN
    ALTER TABLE dbo.BulkFormRequests DROP COLUMN RequestedByName;
    PRINT 'Dropped BulkFormRequests.RequestedByName';
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.BulkFormRequests') AND name = 'ApprovedByName')
BEGIN
    ALTER TABLE dbo.BulkFormRequests DROP COLUMN ApprovedByName;
    PRINT 'Dropped BulkFormRequests.ApprovedByName';
END
GO

PRINT 'DropLegacyIdentitySnapshots migration completed.';
GO