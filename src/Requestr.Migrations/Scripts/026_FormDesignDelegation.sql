IF COL_LENGTH('dbo.FormDefinitions', 'DesignVersion') IS NULL
BEGIN
    ALTER TABLE dbo.FormDefinitions ADD DesignVersion rowversion NOT NULL;
END;
GO

IF OBJECT_ID('dbo.FormDesignHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FormDesignHistory (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FormDefinitionId int NOT NULL,
        ChangedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        ChangedBy nvarchar(255) NOT NULL,
        PreviousDesign nvarchar(max) NOT NULL,
        NewDesign nvarchar(max) NOT NULL,
        CONSTRAINT FK_FormDesignHistory_FormDefinitions FOREIGN KEY (FormDefinitionId) REFERENCES dbo.FormDefinitions(Id),
        CONSTRAINT CK_FormDesignHistory_PreviousDesign CHECK (ISJSON(PreviousDesign) = 1),
        CONSTRAINT CK_FormDesignHistory_NewDesign CHECK (ISJSON(NewDesign) = 1)
    );
    CREATE INDEX IX_FormDesignHistory_FormDefinitionId_ChangedAt
        ON dbo.FormDesignHistory(FormDefinitionId, ChangedAt);
END;
GO