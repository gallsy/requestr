IF COL_LENGTH('dbo.FormDefinitions', 'HideRequestComments') IS NULL
BEGIN
    ALTER TABLE dbo.FormDefinitions ADD HideRequestComments bit NOT NULL
        CONSTRAINT DF_FormDefinitions_HideRequestComments DEFAULT (0);
END;