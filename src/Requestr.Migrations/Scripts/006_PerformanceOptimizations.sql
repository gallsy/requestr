-- Performance Optimizations Migration
-- This script adds indexes and optimizations for better performance
-- Migration: 006

USE RequestrApp;
GO

PRINT 'Adding performance indexes...';

-- Composite index for WorkflowInstances lookup by FormRequestId and Status
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowInstances_FormRequestId_Status')
BEGIN
    CREATE INDEX IX_WorkflowInstances_FormRequestId_Status 
    ON WorkflowInstances (FormRequestId, Status);
    PRINT 'Created composite index IX_WorkflowInstances_FormRequestId_Status';
END
GO

-- Composite index for WorkflowStepInstances lookup by WorkflowInstanceId and Status
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowStepInstances_WorkflowInstanceId_Status')
BEGIN
    CREATE INDEX IX_WorkflowStepInstances_WorkflowInstanceId_Status 
    ON WorkflowStepInstances (WorkflowInstanceId, Status);
    PRINT 'Created composite index IX_WorkflowStepInstances_WorkflowInstanceId_Status';
END
GO

-- Composite index for WorkflowSteps lookup by WorkflowDefinitionId and StepType
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowSteps_WorkflowDefinitionId_StepType')
BEGIN
    CREATE INDEX IX_WorkflowSteps_WorkflowDefinitionId_StepType 
    ON WorkflowSteps (WorkflowDefinitionId, StepType);
    PRINT 'Created composite index IX_WorkflowSteps_WorkflowDefinitionId_StepType';
END
GO

-- Include index for WorkflowDefinitions with commonly queried columns
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowDefinitions_FormDefinitionId_IsActive_Include')
BEGIN
    CREATE INDEX IX_WorkflowDefinitions_FormDefinitionId_IsActive_Include 
    ON WorkflowDefinitions (FormDefinitionId, IsActive)
    INCLUDE (Id, Name, Version);
    PRINT 'Created covering index IX_WorkflowDefinitions_FormDefinitionId_IsActive_Include';
END
GO

-- Additional FormRequests indexes for better performance
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FormRequests_FormDefinitionId_Status')
BEGIN
    CREATE INDEX IX_FormRequests_FormDefinitionId_Status 
    ON FormRequests (FormDefinitionId, Status);
    PRINT 'Created composite index IX_FormRequests_FormDefinitionId_Status';
END
GO

-- Index for FormRequestHistory
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FormRequestHistory_FormRequestId_ChangedAt')
BEGIN
    CREATE INDEX IX_FormRequestHistory_FormRequestId_ChangedAt 
    ON FormRequestHistory (FormRequestId, ChangedAt);
    PRINT 'Created composite index IX_FormRequestHistory_FormRequestId_ChangedAt';
END
GO

-- Index for FormFields
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FormFields_FormDefinitionId_DisplayOrder')
BEGIN
    CREATE INDEX IX_FormFields_FormDefinitionId_DisplayOrder 
    ON FormFields (FormDefinitionId, DisplayOrder);
    PRINT 'Created composite index IX_FormFields_FormDefinitionId_DisplayOrder';
END
GO

-- Update statistics for better query optimization
PRINT 'Updating table statistics...';
IF EXISTS (SELECT * FROM sysobjects WHERE name='WorkflowDefinitions' AND xtype='U')
    UPDATE STATISTICS WorkflowDefinitions;

IF EXISTS (SELECT * FROM sysobjects WHERE name='WorkflowSteps' AND xtype='U')
    UPDATE STATISTICS WorkflowSteps;

IF EXISTS (SELECT * FROM sysobjects WHERE name='WorkflowTransitions' AND xtype='U')
    UPDATE STATISTICS WorkflowTransitions;

IF EXISTS (SELECT * FROM sysobjects WHERE name='WorkflowStepFieldConfigurations' AND xtype='U')
    UPDATE STATISTICS WorkflowStepFieldConfigurations;

IF EXISTS (SELECT * FROM sysobjects WHERE name='WorkflowInstances' AND xtype='U')
    UPDATE STATISTICS WorkflowInstances;

IF EXISTS (SELECT * FROM sysobjects WHERE name='WorkflowStepInstances' AND xtype='U')
    UPDATE STATISTICS WorkflowStepInstances;

UPDATE STATISTICS FormDefinitions;
UPDATE STATISTICS FormFields;
UPDATE STATISTICS FormRequests;
UPDATE STATISTICS FormRequestHistory;

PRINT 'Performance optimizations completed successfully.';
GO
