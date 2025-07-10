-- Performance Optimizations for Workflow System
-- This script addresses timeout issues in workflow creation

USE RequestrApp;
GO

-- Add additional indexes to improve query performance
PRINT 'Adding performance indexes...';

-- Composite index for WorkflowInstances lookup by FormRequestId and Status
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowInstances_FormRequestId_Status')
BEGIN
    CREATE INDEX IX_WorkflowInstances_FormRequestId_Status 
    ON WorkflowInstances (FormRequestId, Status);
    PRINT 'Created composite index IX_WorkflowInstances_FormRequestId_Status';
END

-- Composite index for WorkflowStepInstances lookup by WorkflowInstanceId and Status
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowStepInstances_WorkflowInstanceId_Status')
BEGIN
    CREATE INDEX IX_WorkflowStepInstances_WorkflowInstanceId_Status 
    ON WorkflowStepInstances (WorkflowInstanceId, Status);
    PRINT 'Created composite index IX_WorkflowStepInstances_WorkflowInstanceId_Status';
END

-- Composite index for WorkflowSteps lookup by WorkflowDefinitionId and StepType
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowSteps_WorkflowDefinitionId_StepType')
BEGIN
    CREATE INDEX IX_WorkflowSteps_WorkflowDefinitionId_StepType 
    ON WorkflowSteps (WorkflowDefinitionId, StepType);
    PRINT 'Created composite index IX_WorkflowSteps_WorkflowDefinitionId_StepType';
END

-- Include index for WorkflowDefinitions with commonly queried columns
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowDefinitions_FormDefinitionId_IsActive_Include')
BEGIN
    CREATE INDEX IX_WorkflowDefinitions_FormDefinitionId_IsActive_Include 
    ON WorkflowDefinitions (FormDefinitionId, IsActive)
    INCLUDE (Id, Name, Version);
    PRINT 'Created covering index IX_WorkflowDefinitions_FormDefinitionId_IsActive_Include';
END

-- Update statistics for better query optimization
PRINT 'Updating table statistics...';
UPDATE STATISTICS WorkflowDefinitions;
UPDATE STATISTICS WorkflowSteps;
UPDATE STATISTICS WorkflowTransitions;
UPDATE STATISTICS WorkflowStepFieldConfigurations;
UPDATE STATISTICS WorkflowInstances;
UPDATE STATISTICS WorkflowStepInstances;
UPDATE STATISTICS FormRequests;
UPDATE STATISTICS FormDefinitions;

-- Check for fragmentation and rebuild indexes if needed
PRINT 'Checking index fragmentation...';
DECLARE @sql NVARCHAR(MAX) = '';
SELECT @sql = @sql + 
    'ALTER INDEX ' + i.name + ' ON ' + SCHEMA_NAME(t.schema_id) + '.' + t.name + 
    CASE 
        WHEN s.avg_fragmentation_in_percent > 30 THEN ' REBUILD;' + CHAR(13)
        WHEN s.avg_fragmentation_in_percent > 10 THEN ' REORGANIZE;' + CHAR(13)
        ELSE ''
    END
FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'SAMPLED') s
INNER JOIN sys.indexes i ON s.object_id = i.object_id AND s.index_id = i.index_id
INNER JOIN sys.tables t ON i.object_id = t.object_id
WHERE s.avg_fragmentation_in_percent > 10
    AND i.name IS NOT NULL
    AND t.name IN ('WorkflowDefinitions', 'WorkflowSteps', 'WorkflowTransitions', 
                   'WorkflowStepFieldConfigurations', 'WorkflowInstances', 
                   'WorkflowStepInstances', 'FormRequests', 'FormDefinitions');

IF LEN(@sql) > 0
BEGIN
    PRINT 'Rebuilding/reorganizing fragmented indexes...';
    EXEC sp_executesql @sql;
END
ELSE
BEGIN
    PRINT 'No index maintenance required - fragmentation levels are acceptable.';
END

-- Analyze table sizes to identify potential performance bottlenecks
PRINT 'Table size analysis:';
SELECT 
    t.name AS TableName,
    p.[rows] AS [RowCount],
    CAST(ROUND(((SUM(a.total_pages) * 8) / 1024.00), 2) AS NUMERIC(36, 2)) AS [TotalSizeMB],
    CAST(ROUND(((SUM(a.used_pages) * 8) / 1024.00), 2) AS NUMERIC(36, 2)) AS [UsedSizeMB]
FROM sys.tables t
INNER JOIN sys.indexes i ON t.object_id = i.object_id
INNER JOIN sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
INNER JOIN sys.allocation_units a ON p.partition_id = a.container_id
WHERE t.name IN ('WorkflowDefinitions', 'WorkflowSteps', 'WorkflowTransitions', 
                 'WorkflowStepFieldConfigurations', 'WorkflowInstances', 
                 'WorkflowStepInstances', 'FormRequests', 'FormDefinitions')
    AND i.type_desc = 'CLUSTERED'
GROUP BY t.name, p.[rows]
ORDER BY [TotalSizeMB] DESC;

PRINT 'Performance optimization script completed.';
