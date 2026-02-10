-- Add computed value support to form fields
-- ComputedValueType: 0=None, 1=CurrentDateTimeUtc, 2=CurrentDateTimeLocal, 3=CurrentUserId, 4=CurrentUserDisplayName, 5=CurrentUserEmail, 6=NewGuid
-- ComputedValueApplyMode: 0=InsertAndUpdate, 1=InsertOnly, 2=UpdateOnly

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('FormFields') AND name = 'ComputedValueType')
BEGIN
    ALTER TABLE FormFields ADD ComputedValueType int NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('FormFields') AND name = 'ComputedValueApplyMode')
BEGIN
    ALTER TABLE FormFields ADD ComputedValueApplyMode int NOT NULL DEFAULT 0;
END
