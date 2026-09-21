using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Services.FormRequests;
using Requestr.Core.Services.Workflow;
using Requestr.Core.Utilities;
using Requestr.Web.Authorization;

namespace Requestr.Web.Services;

public interface IFormLookupService
{
    Task<IReadOnlyList<LookupOption>> SearchAsync(int formId, string fieldName, string? search, int? requestId = null, CancellationToken cancellationToken = default);
    Task<LookupOption?> ResolveAsync(int formId, string fieldName, string value, int? requestId = null, CancellationToken cancellationToken = default, int? bulkRequestId = null);
    Task<IReadOnlyList<LookupOption>> SearchDependentAsync(int formId, string fieldName, string? search, string? parentValue, int? requestId = null, CancellationToken cancellationToken = default);
    Task<LookupOption?> ResolveDependentAsync(int formId, string fieldName, string value, string? parentValue, int? requestId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupOption>> SearchPreviewAsync(FormDefinition form, string fieldName, string? search, string? parentValue, CancellationToken cancellationToken = default);
    Task<LookupOption?> ResolvePreviewAsync(FormDefinition form, string fieldName, string value, string? parentValue, CancellationToken cancellationToken = default);
    Task<LookupFilterPage> SearchFilterLevelAsync(int formId, string fieldName, int level, IReadOnlyList<string> filters, string? parentValue, string? search, int offset = 0, int? requestId = null, FormDefinition? draft = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupOption>> SearchFilteredAsync(int formId, string fieldName, string? search, string? parentValue, IReadOnlyList<string> filters, int? requestId = null, FormDefinition? draft = null, CancellationToken cancellationToken = default);
    Task<LookupOption?> ResolveFilteredAsync(int formId, string fieldName, string value, string? parentValue, IReadOnlyList<string> filters, int? requestId = null, FormDefinition? draft = null, CancellationToken cancellationToken = default);
    Task<LookupSelectionPath?> ResolvePathAsync(int formId, string fieldName, string value, string? parentValue, bool enforceParent = true, int? requestId = null, FormDefinition? draft = null, CancellationToken cancellationToken = default);
}

public class FormLookupService(AuthenticationStateProvider authentication, IFormAuthorizationService authorization,
    IFormDefinitionService definitions, IFormRequestQueryService requests, IWorkflowInstanceService workflows,
    ILookupDataService lookups, IBulkFormRequestService bulkRequests) : IFormLookupService
{
    public async Task<IReadOnlyList<LookupOption>> SearchAsync(int formId, string fieldName, string? search, int? requestId = null, CancellationToken cancellationToken = default)
    {
        var (form, field) = await GetAuthorizedFieldAsync(formId, fieldName, requestId);
        cancellationToken.ThrowIfCancellationRequested();
        return await lookups.SearchAsync(form, field, search, cancellationToken);
    }

    public async Task<LookupOption?> ResolveAsync(int formId, string fieldName, string value, int? requestId = null, CancellationToken cancellationToken = default, int? bulkRequestId = null)
    {
        var (form, field) = await GetAuthorizedFieldAsync(formId, fieldName, requestId, bulkRequestId);
        cancellationToken.ThrowIfCancellationRequested();
        return await lookups.ResolveAsync(form, field, value, cancellationToken);
    }

    public async Task<IReadOnlyList<LookupOption>> SearchDependentAsync(int formId, string fieldName, string? search, string? parentValue, int? requestId = null, CancellationToken cancellationToken = default)
    {
        var (form, field) = await GetAuthorizedFieldAsync(formId, fieldName, requestId);
        return await lookups.SearchDependentAsync(form, field, search, parentValue, cancellationToken);
    }

    public async Task<LookupOption?> ResolveDependentAsync(int formId, string fieldName, string value, string? parentValue, int? requestId = null, CancellationToken cancellationToken = default)
    {
        var (form, field) = await GetAuthorizedFieldAsync(formId, fieldName, requestId);
        return await lookups.ResolveDependentAsync(form, field, value, parentValue, cancellationToken);
    }

    public async Task<IReadOnlyList<LookupOption>> SearchPreviewAsync(FormDefinition form, string fieldName, string? search, string? parentValue, CancellationToken cancellationToken = default)
    {
        var field = await GetPreviewFieldAsync(form, fieldName, cancellationToken);
        return await lookups.SearchDependentAsync(form, field, search, parentValue, cancellationToken);
    }

    public async Task<LookupOption?> ResolvePreviewAsync(FormDefinition form, string fieldName, string value, string? parentValue, CancellationToken cancellationToken = default)
    {
        var field = await GetPreviewFieldAsync(form, fieldName, cancellationToken);
        return await lookups.ResolveDependentAsync(form, field, value, parentValue, cancellationToken);
    }

    public async Task<LookupFilterPage> SearchFilterLevelAsync(int formId, string fieldName, int level, IReadOnlyList<string> filters, string? parentValue, string? search, int offset = 0, int? requestId = null, FormDefinition? draft = null, CancellationToken cancellationToken = default)
    {
        var (form, field) = await GetFilterFieldAsync(formId, fieldName, requestId, draft, cancellationToken);
        return await lookups.SearchFilterLevelAsync(form, field, level, filters, parentValue, search, offset, cancellationToken);
    }

    public async Task<IReadOnlyList<LookupOption>> SearchFilteredAsync(int formId, string fieldName, string? search, string? parentValue, IReadOnlyList<string> filters, int? requestId = null, FormDefinition? draft = null, CancellationToken cancellationToken = default)
    {
        var (form, field) = await GetFilterFieldAsync(formId, fieldName, requestId, draft, cancellationToken);
        return await lookups.SearchFilteredAsync(form, field, search, parentValue, filters, cancellationToken);
    }

    public async Task<LookupOption?> ResolveFilteredAsync(int formId, string fieldName, string value, string? parentValue, IReadOnlyList<string> filters, int? requestId = null, FormDefinition? draft = null, CancellationToken cancellationToken = default)
    {
        var (form, field) = await GetFilterFieldAsync(formId, fieldName, requestId, draft, cancellationToken);
        return await lookups.ResolveFilteredAsync(form, field, value, parentValue, filters, cancellationToken);
    }

    public async Task<LookupSelectionPath?> ResolvePathAsync(int formId, string fieldName, string value, string? parentValue, bool enforceParent = true, int? requestId = null, FormDefinition? draft = null, CancellationToken cancellationToken = default)
    {
        var (form, field) = await GetFilterFieldAsync(formId, fieldName, requestId, draft, cancellationToken);
        return await lookups.ResolvePathAsync(form, field, value, parentValue, enforceParent, cancellationToken);
    }

    private async Task<(FormDefinition, FormField)> GetFilterFieldAsync(int formId, string fieldName, int? requestId, FormDefinition? draft, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (draft != null) return (draft, await GetPreviewFieldAsync(draft, fieldName, cancellationToken));
        return await GetAuthorizedFieldAsync(formId, fieldName, requestId);
    }

    private async Task<FormField> GetPreviewFieldAsync(FormDefinition form, string fieldName, CancellationToken cancellationToken)
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true || !ClaimsHelper.GetUserRoles(user).Contains("Admin")) throw new UnauthorizedAccessException();
        await lookups.ValidateConfigurationAsync(form, cancellationToken);
        return form.Fields.Single(field => field.Name == fieldName && field.OptionSource == FieldOptionSource.DatabaseLookup);
    }

    private async Task<(FormDefinition, FormField)> GetAuthorizedFieldAsync(int formId, string fieldName, int? requestId, int? bulkRequestId = null)
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException();
        var roles = ClaimsHelper.GetUserRoles(user);
        var userId = user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst("oid")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var allowed = roles.Contains("Admin") || await authorization.UserHasAnyPermissionAsync(user, formId,
            FormPermissionType.CreateRequest, FormPermissionType.UpdateRequest, FormPermissionType.DeleteRequest,
            FormPermissionType.ViewData, FormPermissionType.BulkUploadCsv, FormPermissionType.BulkActions);
        FormRequest? request = null;
        if (!allowed && requestId.HasValue)
        {
            request = await requests.GetByIdAsync(requestId.Value);
            if (request?.FormDefinitionId != formId) throw new UnauthorizedAccessException();
            allowed = !string.IsNullOrEmpty(userId) && (request.RequestedBy == userId ||
                (request.WorkflowInstanceId.HasValue && await workflows.HasUserParticipatedInWorkflowAsync(userId, roles, request.WorkflowInstanceId.Value)));
        }
        BulkFormRequest? bulkRequest = null;
        if (!allowed && bulkRequestId.HasValue)
        {
            bulkRequest = await bulkRequests.GetBulkFormRequestByIdAsync(bulkRequestId.Value);
            if (bulkRequest?.FormDefinitionId != formId) throw new UnauthorizedAccessException();
            allowed = !string.IsNullOrEmpty(userId) && (bulkRequest.RequestedBy == userId ||
                (bulkRequest.WorkflowInstanceId.HasValue && await workflows.HasUserParticipatedInWorkflowAsync(userId, roles, bulkRequest.WorkflowInstanceId.Value)));
        }
        if (!allowed && request == null && bulkRequest == null) throw new UnauthorizedAccessException();
        var form = await definitions.GetFormDefinitionAsync(formId) ?? throw new UnauthorizedAccessException();
        if (!allowed && (request != null || bulkRequest != null))
            allowed = form.ApproverRoles.Any(role => roles.Contains(role, StringComparer.OrdinalIgnoreCase));
        if (!allowed || form.IsDeleted) throw new UnauthorizedAccessException();
        var field = form.Fields.FirstOrDefault(field => field.Name == fieldName && field.OptionSource == FieldOptionSource.DatabaseLookup)
            ?? throw new UnauthorizedAccessException();
        return (form, field);
    }
}