-- Canonical target-record key (JSON of primary key values) so requests can be looked up per record.
-- Existing rows are backfilled by the web app at startup because primary keys live in the target databases.
-- The legacy FormRequests.AppliedRecordKey column is only read by that backfill and can be dropped afterwards.
IF COL_LENGTH('dbo.FormRequests', 'RecordKey') IS NULL
BEGIN
    ALTER TABLE dbo.FormRequests ADD RecordKey nvarchar(450) NULL;
END;
GO

IF COL_LENGTH('dbo.BulkFormRequestItems', 'RecordKey') IS NULL
BEGIN
    ALTER TABLE dbo.BulkFormRequestItems ADD RecordKey nvarchar(450) NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FormRequests_RecordKey' AND object_id = OBJECT_ID('dbo.FormRequests'))
BEGIN
    CREATE INDEX IX_FormRequests_RecordKey ON dbo.FormRequests(RecordKey) INCLUDE (FormDefinitionId);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BulkFormRequestItems_RecordKey' AND object_id = OBJECT_ID('dbo.BulkFormRequestItems'))
BEGIN
    CREATE INDEX IX_BulkFormRequestItems_RecordKey ON dbo.BulkFormRequestItems(RecordKey) INCLUDE (BulkFormRequestId);
END;
GO

-- ViewRecordHistory (12): grant to Admin wherever Admin can view data, matching new-form defaults.
INSERT INTO dbo.FormPermissions (FormDefinitionId, RoleName, PermissionType, IsGranted, CreatedBy)
SELECT p.FormDefinitionId, p.RoleName, 12, 1, 'System Migration'
FROM dbo.FormPermissions p
WHERE p.RoleName = 'Admin'
  AND p.PermissionType = 10
  AND p.IsGranted = 1
  AND NOT EXISTS (
      SELECT 1 FROM dbo.FormPermissions existing
      WHERE existing.FormDefinitionId = p.FormDefinitionId
        AND existing.RoleName = p.RoleName
        AND existing.PermissionType = 12);
GO
