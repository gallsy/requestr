IF COL_LENGTH('dbo.FormFields', 'OptionSource') IS NULL
BEGIN
    ALTER TABLE dbo.FormFields ADD
        OptionSource int NOT NULL CONSTRAINT DF_FormFields_OptionSource DEFAULT (0)
            CONSTRAINT CK_FormFields_OptionSource CHECK (OptionSource IN (0, 1)),
        LookupSchema nvarchar(128) NULL,
        LookupTable nvarchar(128) NULL,
        LookupKeyColumn nvarchar(128) NULL,
        LookupLabelColumn nvarchar(128) NULL;
END;