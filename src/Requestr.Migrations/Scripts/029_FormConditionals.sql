IF COL_LENGTH('dbo.FormFields', 'LookupParentField') IS NULL
BEGIN
    ALTER TABLE dbo.FormFields ADD LookupParentField nvarchar(255) NULL, LookupFilterColumn nvarchar(128) NULL;
END;
ALTER TABLE dbo.FormFields ALTER COLUMN VisibilityCondition nvarchar(max) NULL;