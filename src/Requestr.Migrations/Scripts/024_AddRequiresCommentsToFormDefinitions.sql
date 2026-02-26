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

-- Migrate data from old RequiresComments column (if it exists) and drop it
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'RequiresComments')
BEGIN
    UPDATE FormDefinitions
    SET RequiresRequestComments = RequiresComments,
        RequiresApprovalComments = RequiresComments;
    PRINT 'Migrated RequiresComments data to new columns.';

    -- Drop the default constraint before dropping the column
    DECLARE @constraintName NVARCHAR(256);
    SELECT @constraintName = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE dc.parent_object_id = OBJECT_ID('FormDefinitions') AND c.name = 'RequiresComments';

    IF @constraintName IS NOT NULL
    BEGIN
        EXEC('ALTER TABLE FormDefinitions DROP CONSTRAINT ' + @constraintName);
        PRINT 'Dropped default constraint on RequiresComments.';
    END

    ALTER TABLE FormDefinitions DROP COLUMN RequiresComments;
    PRINT 'Dropped old RequiresComments column.';
END
GO
