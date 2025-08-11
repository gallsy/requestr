-- Workflow Form Configuration System Migration
-- This script adds the form-workflow configuration system
-- Migration: 008

USE Requestr;
GO

-- FormWorkflowStepConfigurations table - stores form-specific configuration for workflow steps
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FormWorkflowStepConfigurations' AND xtype='U')
BEGIN
    CREATE TABLE FormWorkflowStepConfigurations (
        Id int IDENTITY(1,1) PRIMARY KEY,
        FormDefinitionId int NOT NULL,
        WorkflowDefinitionId int NOT NULL,
        WorkflowStepId nvarchar(50) NOT NULL, -- References WorkflowSteps.StepId
        StepConfiguration nvarchar(max) NOT NULL, -- JSON configuration specific to this form
        PreviousConfiguration nvarchar(max) NULL, -- For rollback capability
        IsActive bit NOT NULL DEFAULT 1,
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy nvarchar(255) NOT NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(255) NULL,
        FOREIGN KEY (FormDefinitionId) REFERENCES FormDefinitions(Id) ON DELETE CASCADE,
        FOREIGN KEY (WorkflowDefinitionId) REFERENCES WorkflowDefinitions(Id) ON DELETE NO ACTION,
        UNIQUE (FormDefinitionId, WorkflowDefinitionId, WorkflowStepId),
        INDEX IX_FormWorkflowStepConfigurations_FormDefinitionId (FormDefinitionId),
        INDEX IX_FormWorkflowStepConfigurations_WorkflowDefinitionId (WorkflowDefinitionId),
        INDEX IX_FormWorkflowStepConfigurations_IsActive (IsActive)
    );
    PRINT 'FormWorkflowStepConfigurations table created successfully.';
END
GO

-- Update FormDefinitions to track workflow assignment more explicitly
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'WorkflowAssignedAt')
BEGIN
    ALTER TABLE FormDefinitions ADD 
        WorkflowAssignedAt datetime2 NULL,
        WorkflowAssignedBy nvarchar(255) NULL;
    PRINT 'Added workflow assignment tracking to FormDefinitions.';
END
GO

PRINT 'Workflow form configuration system migration completed successfully.';
GO
