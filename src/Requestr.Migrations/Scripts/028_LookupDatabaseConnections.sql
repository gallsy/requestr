IF COL_LENGTH('dbo.FormFields', 'LookupDatabaseConnectionName') IS NULL
BEGIN
    ALTER TABLE dbo.FormFields ADD LookupDatabaseConnectionName nvarchar(255) NULL;
END;