using Requestr.Core.Models;

namespace Requestr.Core.Services.Workflow;

/// <summary>
/// Executes HTTP webhook calls as part of a workflow step.
/// </summary>
public interface IWebhookExecutionService
{
    /// <summary>
    /// Executes a webhook call using the provided configuration and form request data.
    /// </summary>
    /// <param name="config">The webhook step configuration.</param>
    /// <param name="request">The form request whose data is used for variable substitution.</param>
    /// <param name="formDefinition">The form definition (optional, for system variables).</param>
    /// <returns>A result indicating success/failure, status code, and response details.</returns>
    Task<WebhookResult> ExecuteAsync(WebhookStepConfiguration config, FormRequest request, FormDefinition? formDefinition = null);
}
