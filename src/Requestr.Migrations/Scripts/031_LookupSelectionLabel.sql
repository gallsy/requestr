IF COL_LENGTH('dbo.FormFields', 'LookupSelectionLabel') IS NULL
BEGIN
    ALTER TABLE dbo.FormFields ADD LookupSelectionLabel nvarchar(128) NULL;
END;