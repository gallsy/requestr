-- Migrate data from old RequiresComments column to the split columns and drop it.
-- Handles the case where 024 created RequiresComments but was later split into two columns.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'RequiresRequestComments')
BEGIN
    ALTER TABLE FormDefinitions ADD RequiresRequestComments bit NOT NULL DEFAULT 0;
    PRINT 'Added RequiresRequestComments column.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'RequiresApprovalComments')
BEGIN
    ALTER TABLE FormDefinitions ADD RequiresApprovalComments bit NOT NULL DEFAULT 0;
    PRINT 'Added RequiresApprovalComments column.';
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'RequiresComments')
BEGIN
    -- Copy existing values to both new columns
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
ELSE
BEGIN
    PRINT 'RequiresComments column does not exist — nothing to migrate.';
END
GO
