-- Workflow System Database Schema
-- This script adds workflow management capabilities to the Requestr application
-- Run this after the main requestr-schema.sql

USE RequestrApp;
GO

-- WorkflowDefinitions table - stores the workflow template for each form
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='WorkflowDefinitions' AND xtype='U')
BEGIN
    CREATE TABLE WorkflowDefinitions (
        Id int IDENTITY(1,1) PRIMARY KEY,
        FormDefinitionId int NOT NULL,
        Name nvarchar(255) NOT NULL,
        Description nvarchar(max) NULL,
        IsActive bit NOT NULL DEFAULT 1,
        Version int NOT NULL DEFAULT 1,
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy nvarchar(255) NOT NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(255) NULL,
        FOREIGN KEY (FormDefinitionId) REFERENCES FormDefinitions(Id) ON DELETE CASCADE,
        INDEX IX_WorkflowDefinitions_FormDefinitionId (FormDefinitionId),
        INDEX IX_WorkflowDefinitions_IsActive (IsActive)
    );
    PRINT 'WorkflowDefinitions table created successfully.';
END
GO

-- WorkflowSteps table - stores individual steps within a workflow
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='WorkflowSteps' AND xtype='U')
BEGIN
    CREATE TABLE WorkflowSteps (
        Id int IDENTITY(1,1) PRIMARY KEY,
        WorkflowDefinitionId int NOT NULL,
        StepId nvarchar(50) NOT NULL, -- Unique identifier within the workflow (e.g., "step1", "approval1")
        StepType nvarchar(50) NOT NULL, -- 'Approval', 'Parallel', 'Branch', 'Start', 'End'
        Name nvarchar(255) NOT NULL,
        Description nvarchar(max) NULL,
        AssignedRoles nvarchar(max) NULL, -- JSON array of Entra roles for approval steps
        PositionX int NOT NULL DEFAULT 0, -- X coordinate for designer
        PositionY int NOT NULL DEFAULT 0, -- Y coordinate for designer
        Configuration nvarchar(max) NULL, -- JSON configuration specific to step type
        IsRequired bit NOT NULL DEFAULT 1,
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (WorkflowDefinitionId) REFERENCES WorkflowDefinitions(Id) ON DELETE CASCADE,
        INDEX IX_WorkflowSteps_WorkflowDefinitionId (WorkflowDefinitionId),
        INDEX IX_WorkflowSteps_StepType (StepType),
        UNIQUE (WorkflowDefinitionId, StepId)
    );
    PRINT 'WorkflowSteps table created successfully.';
END
GO

-- WorkflowTransitions table - defines how steps connect to each other
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='WorkflowTransitions' AND xtype='U')
BEGIN
    CREATE TABLE WorkflowTransitions (
        Id int IDENTITY(1,1) PRIMARY KEY,
        WorkflowDefinitionId int NOT NULL,
        FromStepId nvarchar(50) NOT NULL, -- Source step
        ToStepId nvarchar(50) NOT NULL, -- Destination step
        Condition nvarchar(max) NULL, -- JSON condition for branch steps
        Name nvarchar(255) NULL, -- Display name for the transition
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (WorkflowDefinitionId) REFERENCES WorkflowDefinitions(Id) ON DELETE CASCADE,
        INDEX IX_WorkflowTransitions_WorkflowDefinitionId (WorkflowDefinitionId),
        INDEX IX_WorkflowTransitions_FromStepId (FromStepId),
        INDEX IX_WorkflowTransitions_ToStepId (ToStepId)
    );
    PRINT 'WorkflowTransitions table created successfully.';
END
GO

-- WorkflowStepFieldConfigurations table - controls field behavior per step
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='WorkflowStepFieldConfigurations' AND xtype='U')
BEGIN
    CREATE TABLE WorkflowStepFieldConfigurations (
        Id int IDENTITY(1,1) PRIMARY KEY,
        WorkflowStepId int NOT NULL,
        FieldName nvarchar(255) NOT NULL,
        IsVisible bit NOT NULL DEFAULT 1,
        IsReadOnly bit NOT NULL DEFAULT 0,
        IsRequired bit NOT NULL DEFAULT 0,
        ValidationRules nvarchar(max) NULL, -- JSON validation rules specific to this step
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (WorkflowStepId) REFERENCES WorkflowSteps(Id) ON DELETE CASCADE,
        INDEX IX_WorkflowStepFieldConfigurations_WorkflowStepId (WorkflowStepId),
        INDEX IX_WorkflowStepFieldConfigurations_FieldName (FieldName),
        UNIQUE (WorkflowStepId, FieldName)
    );
    PRINT 'WorkflowStepFieldConfigurations table created successfully.';
END
GO

-- WorkflowInstances table - tracks workflow execution for each form request
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='WorkflowInstances' AND xtype='U')
BEGIN
    CREATE TABLE WorkflowInstances (
        Id int IDENTITY(1,1) PRIMARY KEY,
        FormRequestId int NOT NULL,
        WorkflowDefinitionId int NOT NULL,
        CurrentStepId nvarchar(50) NOT NULL,
        Status nvarchar(50) NOT NULL DEFAULT 'InProgress', -- 'InProgress', 'Completed', 'Cancelled', 'Failed'
        StartedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CompletedAt datetime2 NULL,
        CompletedBy nvarchar(255) NULL,
        FailureReason nvarchar(max) NULL,
        FOREIGN KEY (FormRequestId) REFERENCES FormRequests(Id) ON DELETE CASCADE,
        FOREIGN KEY (WorkflowDefinitionId) REFERENCES WorkflowDefinitions(Id),
        INDEX IX_WorkflowInstances_FormRequestId (FormRequestId),
        INDEX IX_WorkflowInstances_WorkflowDefinitionId (WorkflowDefinitionId),
        INDEX IX_WorkflowInstances_Status (Status),
        INDEX IX_WorkflowInstances_CurrentStepId (CurrentStepId)
    );
    PRINT 'WorkflowInstances table created successfully.';
END
GO

-- WorkflowStepInstances table - tracks execution of individual steps
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='WorkflowStepInstances' AND xtype='U')
BEGIN
    CREATE TABLE WorkflowStepInstances (
        Id int IDENTITY(1,1) PRIMARY KEY,
        WorkflowInstanceId int NOT NULL,
        StepId nvarchar(50) NOT NULL,
        Status nvarchar(50) NOT NULL DEFAULT 'Pending', -- 'Pending', 'InProgress', 'Completed', 'Skipped', 'Failed'
        AssignedTo nvarchar(255) NULL, -- User assigned to this step instance
        StartedAt datetime2 NULL,
        CompletedAt datetime2 NULL,
        CompletedBy nvarchar(255) NULL,
        CompletedByName nvarchar(255) NULL,
        Action nvarchar(50) NULL, -- 'Approved', 'Rejected', 'Completed'
        Comments nvarchar(max) NULL,
        FieldValues nvarchar(max) NULL, -- JSON of field values modified in this step
        FOREIGN KEY (WorkflowInstanceId) REFERENCES WorkflowInstances(Id) ON DELETE CASCADE,
        INDEX IX_WorkflowStepInstances_WorkflowInstanceId (WorkflowInstanceId),
        INDEX IX_WorkflowStepInstances_StepId (StepId),
        INDEX IX_WorkflowStepInstances_Status (Status),
        INDEX IX_WorkflowStepInstances_AssignedTo (AssignedTo)
    );
    PRINT 'WorkflowStepInstances table created successfully.';
END
GO

-- Enhanced permissions table for granular access control
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ApplicationPermissions' AND xtype='U')
BEGIN
    CREATE TABLE ApplicationPermissions (
        Id int IDENTITY(1,1) PRIMARY KEY,
        RoleName nvarchar(255) NOT NULL,
        Permission nvarchar(255) NOT NULL, -- 'DataView.Access', 'BulkActions.Execute', 'CSV.Upload', etc.
        ResourceId int NULL, -- Optional: specific form or resource ID
        IsGranted bit NOT NULL DEFAULT 1,
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy nvarchar(255) NOT NULL,
        INDEX IX_ApplicationPermissions_RoleName (RoleName),
        INDEX IX_ApplicationPermissions_Permission (Permission),
        INDEX IX_ApplicationPermissions_ResourceId (ResourceId),
        UNIQUE (RoleName, Permission, ResourceId)
    );
    PRINT 'ApplicationPermissions table created successfully.';
END
GO

-- Update FormDefinitions to support workflow system
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'WorkflowDefinitionId')
BEGIN
    ALTER TABLE FormDefinitions
    ADD WorkflowDefinitionId int NULL;
    
    -- Add foreign key constraint
    ALTER TABLE FormDefinitions
    ADD CONSTRAINT FK_FormDefinitions_WorkflowDefinitionId 
    FOREIGN KEY (WorkflowDefinitionId) REFERENCES WorkflowDefinitions(Id);
    
    -- Add index for performance
    CREATE INDEX IX_FormDefinitions_WorkflowDefinitionId ON FormDefinitions(WorkflowDefinitionId);
    
    PRINT 'Added WorkflowDefinitionId to FormDefinitions table';
END
GO

-- Update FormRequests to track workflow instances
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormRequests') AND name = 'WorkflowInstanceId')
BEGIN
    ALTER TABLE FormRequests
    ADD WorkflowInstanceId int NULL;
    
    -- Add foreign key constraint
    ALTER TABLE FormRequests
    ADD CONSTRAINT FK_FormRequests_WorkflowInstanceId 
    FOREIGN KEY (WorkflowInstanceId) REFERENCES WorkflowInstances(Id);
    
    -- Add index for performance
    CREATE INDEX IX_FormRequests_WorkflowInstanceId ON FormRequests(WorkflowInstanceId);
    
    PRINT 'Added WorkflowInstanceId to FormRequests table';
END
GO

-- Insert default permissions for existing roles
IF NOT EXISTS (SELECT * FROM ApplicationPermissions WHERE RoleName = 'Admin')
BEGIN
    INSERT INTO ApplicationPermissions (RoleName, Permission, IsGranted, CreatedBy) VALUES
    ('Admin', 'DataView.Access', 1, 'System'),
    ('Admin', 'BulkActions.Execute', 1, 'System'),
    ('Admin', 'CSV.Upload', 1, 'System'),
    ('Admin', 'Workflow.Design', 1, 'System'),
    ('Admin', 'Workflow.Manage', 1, 'System'),
    ('FormAdmin', 'DataView.Access', 1, 'System'),
    ('FormAdmin', 'BulkActions.Execute', 1, 'System'),
    ('FormAdmin', 'CSV.Upload', 1, 'System'),
    ('FormAdmin', 'Workflow.Design', 1, 'System'),
    ('DataAdmin', 'DataView.Access', 1, 'System'),
    ('DataAdmin', 'BulkActions.Execute', 1, 'System'),
    ('DataAdmin', 'CSV.Upload', 1, 'System'),
    ('ReferenceDataApprover', 'DataView.Access', 0, 'System'),
    ('ReferenceDataApprover', 'BulkActions.Execute', 0, 'System'),
    ('ReferenceDataApprover', 'CSV.Upload', 0, 'System');
    
    PRINT 'Default permissions inserted successfully.';
END
GO

PRINT 'Workflow system database schema created successfully!'
GO
