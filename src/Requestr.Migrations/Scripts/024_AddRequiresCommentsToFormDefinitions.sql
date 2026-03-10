-- Add RequiresRequestComments and RequiresApprovalComments columns to FormDefinitions
-- When enabled, forces users to enter at least 10 characters in the respective comment fields.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'RequiresRequestComments')
BEGIN
    ALTER TABLE FormDefinitions ADD RequiresRequestComments bit NOT NULL DEFAULT 0;
    PRINT 'Added RequiresRequestComments column to FormDefinitions table.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'RequiresApprovalComments')
BEGIN
    ALTER TABLE FormDefinitions ADD RequiresApprovalComments bit NOT NULL DEFAULT 0;
    PRINT 'Added RequiresApprovalComments column to FormDefinitions table.';
END
GO
