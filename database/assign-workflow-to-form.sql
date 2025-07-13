-- Assign workflow to form definition
-- This will enable workflow-based approvals for bulk requests on this form

USE [RequestrApp]
GO

PRINT 'Assigning workflow to form definition...'

-- Update Form Definition ID 1 to use Workflow Definition ID 1
UPDATE FormDefinitions 
SET WorkflowDefinitionId = 1
WHERE Id = 1;

PRINT 'Workflow assigned successfully. Form Definition 1 now uses Workflow Definition 1 (Location Workflow).'

-- Verify the assignment
SELECT Id, Name, WorkflowDefinitionId 
FROM FormDefinitions 
WHERE Id = 1;

GO
