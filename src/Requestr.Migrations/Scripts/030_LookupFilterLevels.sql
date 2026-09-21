IF COL_LENGTH('dbo.FormFields', 'LookupFilterLevels') IS NULL
BEGIN
    ALTER TABLE dbo.FormFields ADD LookupFilterLevels nvarchar(max) NULL;
END;