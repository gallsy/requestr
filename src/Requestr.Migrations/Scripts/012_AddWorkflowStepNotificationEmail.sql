-- Add NotificationEmail column to WorkflowSteps table
-- This allows step-specific notification emails for approval actions

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('WorkflowSteps') AND name = 'NotificationEmail')
BEGIN
    ALTER TABLE WorkflowSteps ADD NotificationEmail nvarchar(255) NULL;
    PRINT 'Added NotificationEmail column to WorkflowSteps table.';
END
ELSE
BEGIN
    PRINT 'NotificationEmail column already exists in WorkflowSteps table.';
END
GO
