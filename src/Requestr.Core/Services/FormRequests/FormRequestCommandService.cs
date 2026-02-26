using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Repositories;
using Requestr.Core.Services.Workflow;
using Requestr.Core.Validation;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Requestr.Core.Services.FormRequests;

/// <summary>
/// Implementation of form request command operations (create, update, delete).
/// </summary>
public class FormRequestCommandService : IFormRequestCommandService
{
    private readonly IFormRequestRepository _formRequestRepository;
    private readonly IFormRequestHistoryService _historyService;
    private readonly IFormDefinitionService _formDefinitionService;
    private readonly IWorkflowDefinitionQueryService _workflowDefinitionQueryService;
    private readonly IWorkflowInstanceService _workflowInstanceService;
    private readonly IInputValidationService _inputValidationService;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<FormRequestCommandService> _logger;

    public FormRequestCommandService(
        IFormRequestRepository formRequestRepository,
        IFormRequestHistoryService historyService,
        IFormDefinitionService formDefinitionService,
        IWorkflowDefinitionQueryService workflowDefinitionQueryService,
        IWorkflowInstanceService workflowInstanceService,
        IInputValidationService inputValidationService,
        IDbConnectionFactory connectionFactory,
        ILogger<FormRequestCommandService> logger)
    {
        _formRequestRepository = formRequestRepository;
        _historyService = historyService;
        _formDefinitionService = formDefinitionService;
        _workflowDefinitionQueryService = workflowDefinitionQueryService;
        _workflowInstanceService = workflowInstanceService;
        _inputValidationService = inputValidationService;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<FormRequest> CreateAsync(FormRequest formRequest)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Get form definition for validation
            var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formRequest.FormDefinitionId);
            if (formDefinition == null)
            {
                throw new InvalidOperationException("Form definition not found");
            }

            // Validate and sanitize field values
            var validationResult = await _inputValidationService.ValidateFormSubmissionAsync(
                formRequest.FieldValues, formDefinition.Fields);
            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException($"Form validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            // Sanitize comments
            if (!string.IsNullOrEmpty(formRequest.Comments))
            {
                formRequest.Comments = _inputValidationService.SanitizeComments(formRequest.Comments);
            }

            // Check if the form has a workflow definition
            var workflowDefinition = await _workflowDefinitionQueryService.GetWorkflowDefinitionByFormAsync(formRequest.FormDefinitionId);
            
            // Set initial status based on whether workflow exists
            formRequest.Status = workflowDefinition != null ? RequestStatus.Pending : RequestStatus.Approved;

            // Create the form request
            var createdRequest = await _formRequestRepository.CreateAsync(formRequest, connection, transaction);

            // Start workflow if one exists
            if (workflowDefinition != null)
            {
                var workflowInstanceId = await _workflowInstanceService.StartWorkflowAsync(
                    connection, transaction, createdRequest.Id, workflowDefinition.Id, formRequest.RequestedBy);
                createdRequest.WorkflowInstanceId = workflowInstanceId;

                // Update the form request with the workflow instance ID
                await _formRequestRepository.UpdateWorkflowInstanceIdAsync(
                    createdRequest.Id, workflowInstanceId, connection, transaction);

                _logger.LogInformation("Started workflow {WorkflowId} for form request {RequestId}", 
                    workflowInstanceId, createdRequest.Id);
            }
            else
            {
                _logger.LogInformation("Form request {RequestId} auto-approved (no workflow)", createdRequest.Id);
            }

            // Record the creation in history
            await _historyService.RecordChangeAsync(
                createdRequest.Id,
                FormRequestChangeType.Created,
                null,
                new Dictionary<string, object?>
                {
                    { "RequestType", createdRequest.RequestType },
                    { "FieldValues", createdRequest.FieldValues },
                    { "OriginalValues", createdRequest.OriginalValues },
                    { "Status", (int)createdRequest.Status },
                    { "Comments", createdRequest.Comments },
                    { "WorkflowInstanceId", createdRequest.WorkflowInstanceId }
                },
                createdRequest.RequestedBy,
                createdRequest.RequestedByName,
                workflowDefinition != null ? "Request created and workflow started" : "Request created (no workflow)",
                connection,
                transaction
            );

            await transaction.CommitAsync();
            
            return createdRequest;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error creating form request");
            throw;
        }
    }

    public async Task<FormRequest> UpdateAsync(FormRequest formRequest)
    {
        try
        {
            // Get the current version for comparison
            var currentRequest = await _formRequestRepository.GetByIdAsync(formRequest.Id);
            if (currentRequest == null)
            {
                throw new ArgumentException($"Form request with ID {formRequest.Id} not found");
            }

            // Get form definition for validation
            var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formRequest.FormDefinitionId);
            if (formDefinition == null)
            {
                throw new ArgumentException($"Form definition with ID {formRequest.FormDefinitionId} not found");
            }

            // Validate and sanitize input before updating
            var validationResult = await _inputValidationService.ValidateFormSubmissionAsync(
                formRequest.FieldValues, formDefinition.Fields);
            
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Form request update validation failed for request {RequestId}: {Errors}", 
                    formRequest.Id, string.Join(", ", validationResult.Errors));
                throw new ValidationException($"Form update validation failed: {string.Join(", ", validationResult.Errors)}");
            }
            
            // Sanitize field values and comments
            var sanitizedFieldValues = new Dictionary<string, object?>();
            foreach (var field in formDefinition.Fields.Where(f => f.IsVisible))
            {
                if (formRequest.FieldValues.ContainsKey(field.Name))
                {
                    var inputValue = formRequest.FieldValues[field.Name]?.ToString();
                    if (!string.IsNullOrEmpty(inputValue))
                    {
                        var sanitizedValue = InputValidator.SanitizeInput(inputValue, field);
                        sanitizedFieldValues[field.Name] = sanitizedValue;
                    }
                    else
                    {
                        sanitizedFieldValues[field.Name] = formRequest.FieldValues[field.Name];
                    }
                }
            }
            formRequest.FieldValues = sanitizedFieldValues;
            formRequest.Comments = _inputValidationService.SanitizeComments(formRequest.Comments);

            // Update the form request
            var updatedRequest = await _formRequestRepository.UpdateAsync(formRequest);

            // Track detailed field changes
            var fieldChanges = GetDetailedFieldChanges(currentRequest, formRequest);
            
            // Record the changes in history
            var previousValues = new Dictionary<string, object?>
            {
                { "RequestType", currentRequest.RequestType },
                { "Status", (int)currentRequest.Status },
                { "Comments", currentRequest.Comments }
            };

            var newValues = new Dictionary<string, object?>
            {
                { "RequestType", formRequest.RequestType },
                { "Status", (int)formRequest.Status },
                { "Comments", formRequest.Comments }
            };

            if (fieldChanges.Any())
            {
                var changesList = fieldChanges.Select(c => new
                {
                    FieldName = c.FieldName,
                    PreviousValue = c.PreviousValue?.ToString(),
                    NewValue = c.NewValue?.ToString(),
                    ChangeType = c.ChangeType
                }).ToList();
                
                newValues["FieldChanges"] = changesList;
            }

            var changeType = currentRequest.Status != formRequest.Status 
                ? FormRequestChangeType.StatusChanged 
                : FormRequestChangeType.Updated;

            await _historyService.RecordChangeAsync(
                formRequest.Id,
                changeType,
                previousValues,
                newValues,
                formRequest.RequestedBy,
                formRequest.RequestedByName,
                $"Request updated{(fieldChanges.Any() ? $" - {fieldChanges.Count} field(s) changed" : "")}"
            );

            return updatedRequest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating form request {Id}", formRequest.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            return await _formRequestRepository.DeleteAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting form request {Id}", id);
            throw;
        }
    }

    public async Task UpdateStatusAsync(int id, RequestStatus status, string? failureMessage = null)
    {
        try
        {
            await _formRequestRepository.UpdateStatusAsync(id, status, failureMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating status for form request {Id}", id);
            throw;
        }
    }

    public async Task<bool> CancelAsync(int id, string userId, string userName)
    {
        try
        {
            var request = await _formRequestRepository.GetByIdAsync(id);
            if (request == null)
            {
                _logger.LogWarning("Form request {Id} not found for cancellation", id);
                return false;
            }

            if (request.RequestedBy != userId)
            {
                _logger.LogWarning("User {UserId} is not the owner of form request {Id}", userId, id);
                return false;
            }

            if (request.Status != RequestStatus.Pending)
            {
                _logger.LogWarning("Form request {Id} is not in Pending status (Status: {Status})", id, request.Status);
                return false;
            }

            // Cancel the workflow if one exists
            if (request.WorkflowInstanceId.HasValue)
            {
                try
                {
                    await _workflowInstanceService.CancelWorkflowAsync(
                        request.WorkflowInstanceId.Value,
                        userId,
                        $"Cancelled by requester: {userName}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cancelling workflow {WorkflowInstanceId} for request {Id}",
                        request.WorkflowInstanceId.Value, id);
                }
            }

            // Update request status to Cancelled
            await _formRequestRepository.UpdateStatusAsync(id, RequestStatus.Cancelled);

            // Record history
            await _historyService.RecordChangeAsync(
                id,
                FormRequestChangeType.Cancelled,
                null, null,
                userId,
                userName,
                "Request cancelled by requester");

            _logger.LogInformation("Form request {Id} cancelled by {UserId}", id, userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling form request {Id}", id);
            throw;
        }
    }

    public async Task UpdateFieldValuesAsync(int id, Dictionary<string, object?> fieldValues)
    {
        try
        {
            var request = await _formRequestRepository.GetByIdAsync(id);
            if (request == null)
            {
                throw new ArgumentException($"Form request with ID {id} not found");
            }

            foreach (var kvp in fieldValues)
            {
                request.FieldValues[kvp.Key] = kvp.Value;
            }

            await _formRequestRepository.UpdateAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating field values for form request {Id}", id);
            throw;
        }
    }

    private List<FieldChange> GetDetailedFieldChanges(FormRequest currentRequest, FormRequest updatedRequest)
    {
        var changes = new List<FieldChange>();

        var allFieldKeys = currentRequest.FieldValues.Keys
            .Union(updatedRequest.FieldValues.Keys)
            .ToHashSet();

        foreach (var fieldKey in allFieldKeys)
        {
            var currentValue = currentRequest.FieldValues.GetValueOrDefault(fieldKey);
            var newValue = updatedRequest.FieldValues.GetValueOrDefault(fieldKey);

            if (!AreValuesEqual(currentValue, newValue))
            {
                changes.Add(new FieldChange
                {
                    FieldName = fieldKey,
                    PreviousValue = currentValue,
                    NewValue = newValue,
                    ChangeType = "FieldValue",
                    HasPreviousValue = currentValue != null,
                    HasNewValue = newValue != null
                });
            }
        }

        var allOriginalKeys = currentRequest.OriginalValues.Keys
            .Union(updatedRequest.OriginalValues.Keys)
            .ToHashSet();

        foreach (var fieldKey in allOriginalKeys)
        {
            var currentValue = currentRequest.OriginalValues.GetValueOrDefault(fieldKey);
            var newValue = updatedRequest.OriginalValues.GetValueOrDefault(fieldKey);

            if (!AreValuesEqual(currentValue, newValue))
            {
                changes.Add(new FieldChange
                {
                    FieldName = $"Original_{fieldKey}",
                    PreviousValue = currentValue,
                    NewValue = newValue,
                    ChangeType = "OriginalValue",
                    HasPreviousValue = currentValue != null,
                    HasNewValue = newValue != null
                });
            }
        }

        return changes;
    }

    private static bool AreValuesEqual(object? value1, object? value2)
    {
        if (value1 == null && value2 == null) return true;
        if (value1 == null || value2 == null) return false;
        
        var str1 = value1.ToString();
        var str2 = value2.ToString();
        
        return string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
    }
}
