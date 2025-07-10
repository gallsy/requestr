using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

public interface IWorkflowService
{
    // Workflow Definition Management
    Task<WorkflowDefinition?> GetWorkflowDefinitionAsync(int id);
    Task<WorkflowDefinition?> GetWorkflowDefinitionByFormAsync(int formDefinitionId);
    Task<List<WorkflowDefinition>> GetWorkflowDefinitionsAsync();
    Task<WorkflowDefinition> CreateWorkflowDefinitionAsync(WorkflowDefinition workflowDefinition);
    Task<WorkflowDefinition> UpdateWorkflowDefinitionAsync(WorkflowDefinition workflowDefinition);
    Task<bool> DeleteWorkflowDefinitionAsync(int id);
    Task<bool> ActivateWorkflowDefinitionAsync(int id);
    Task<bool> DeactivateWorkflowDefinitionAsync(int id);
    
    // Workflow Instance Management
    Task<WorkflowInstance> StartWorkflowAsync(int formRequestId, int workflowDefinitionId);
    Task<WorkflowInstance?> GetWorkflowInstanceAsync(int id);
    Task<WorkflowInstance?> GetWorkflowInstanceByRequestAsync(int formRequestId);
    Task<List<WorkflowInstance>> GetActiveWorkflowInstancesAsync();
    Task<List<WorkflowInstance>> GetWorkflowInstancesByUserAsync(string userId);
    
    // Step Processing
    Task<bool> CompleteStepAsync(int workflowInstanceId, string stepId, string userId, string userName, WorkflowStepAction action, string? comments = null, Dictionary<string, object?>? fieldValues = null);
    Task<List<WorkflowStepInstance>> GetPendingStepsForUserAsync(string userId, List<string> userRoles);
    Task<WorkflowStepInstance?> GetCurrentStepInstanceAsync(int workflowInstanceId);
    Task<List<WorkflowStepInstance>> GetStepInstancesAsync(int workflowInstanceId);
    
    // Step Field Configuration
    Task<List<WorkflowStepFieldConfiguration>> GetStepFieldConfigurationsAsync(int workflowStepId);
    Task<Dictionary<string, object>> GetEffectiveFieldConfigurationAsync(int workflowInstanceId, string stepId);
    
    // Workflow Navigation
    Task<bool> CanUserAccessStepAsync(string userId, List<string> userRoles, int workflowInstanceId, string stepId);
    Task<string?> GetNextStepIdAsync(int workflowInstanceId, string currentStepId, Dictionary<string, object?> formData);
    Task<List<string>> GetAvailableStepsForUserAsync(string userId, List<string> userRoles, int workflowInstanceId);
    
    // Workflow Action Processing
    Task<WorkflowActionResult> ProcessWorkflowActionAsync(int workflowInstanceId, string actionType, string userId, string? comments = null, Dictionary<string, object?>? fieldUpdates = null);
    Task<WorkflowStepInstance?> GetCurrentWorkflowStepAsync(int workflowInstanceId);
    Task<List<WorkflowStepInstance>> GetCompletedWorkflowStepsAsync(int workflowInstanceId);
}

public interface IWorkflowDesignerService
{
    // Workflow Design Operations
    Task<WorkflowDefinition> CreateEmptyWorkflowAsync(int formDefinitionId, string name, string description);
    Task<WorkflowStep> AddStepAsync(int workflowDefinitionId, WorkflowStepType stepType, string name, int positionX, int positionY);
    Task<WorkflowStep> UpdateStepAsync(int stepId, WorkflowStep stepData);
    Task<bool> DeleteStepAsync(int stepId);
    Task<WorkflowTransition> AddTransitionAsync(int workflowDefinitionId, string fromStepId, string toStepId, string? name = null);
    Task<bool> DeleteTransitionAsync(int transitionId);
    
    // Step Configuration
    Task<WorkflowStepFieldConfiguration> ConfigureStepFieldAsync(int workflowStepId, string fieldName, bool isVisible, bool isReadOnly, bool isRequired);
    Task<bool> DeleteStepFieldConfigurationAsync(int configurationId);
    Task<List<WorkflowStepFieldConfiguration>> GetStepFieldConfigurationsAsync(int workflowStepId);
    
    // Validation
    Task<List<string>> ValidateWorkflowAsync(int workflowDefinitionId);
    Task<bool> IsWorkflowValidAsync(int workflowDefinitionId);
}

public interface IPermissionService
{
    // Permission Management
    Task<List<ApplicationPermission>> GetPermissionsAsync();
    Task<List<ApplicationPermission>> GetPermissionsByRoleAsync(string roleName);
    Task<ApplicationPermission> GrantPermissionAsync(string roleName, string permission, int? resourceId = null);
    Task<bool> RevokePermissionAsync(string roleName, string permission, int? resourceId = null);
    Task<bool> HasPermissionAsync(string roleName, string permission, int? resourceId = null);
    Task<bool> HasPermissionAsync(List<string> userRoles, string permission, int? resourceId = null);
    
    // Specific Permission Checks
    Task<bool> CanAccessDataViewAsync(List<string> userRoles);
    Task<bool> CanExecuteBulkActionsAsync(List<string> userRoles);
    Task<bool> CanUploadCsvAsync(List<string> userRoles);
    Task<bool> CanDesignWorkflowsAsync(List<string> userRoles);
    Task<bool> CanManageWorkflowsAsync(List<string> userRoles);
    
    // Role-based Access
    Task<List<FormDefinition>> GetAccessibleFormsForDataViewAsync(List<string> userRoles);
    Task<bool> CanAccessFormDataViewAsync(List<string> userRoles, int formDefinitionId);
}
