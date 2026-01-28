-- Migration: Add SqlDataType column to FormFields table
-- This stores the original SQL Server data type (e.g., 'nvarchar', 'bit', 'int')
-- for schema drift detection. The DataType column stores UI control types.
--
-- Note: Backfill cannot happen at the database layer because the actual table schemas
-- are in external databases that only the application has connection strings for.
-- The application will populate NULL SqlDataType values on first form save.

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormFields') AND name = 'SqlDataType')
BEGIN
    ALTER TABLE FormFields ADD SqlDataType NVARCHAR(50) NULL;
    PRINT 'Added SqlDataType column to FormFields table.';
END
GO
