using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

public record LookupOption(string Value, string Text);
public record LookupFilterPage(IReadOnlyList<LookupOption> Options, bool HasMore);
public record LookupSelectionPath(LookupOption Option, IReadOnlyList<string> Filters);

public interface ILookupDataService
{
    Task ValidateConfigurationAsync(FormDefinition form, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupOption>> SearchAsync(FormDefinition form, FormField field, string? search, CancellationToken cancellationToken = default);
    Task<LookupOption?> ResolveAsync(FormDefinition form, FormField field, string value, CancellationToken cancellationToken = default);
    Task ValidateValuesAsync(FormDefinition form, Dictionary<string, object?> values, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupOption>> SearchDependentAsync(FormDefinition form, FormField field, string? search, string? parentValue, CancellationToken cancellationToken = default);
    Task<LookupOption?> ResolveDependentAsync(FormDefinition form, FormField field, string value, string? parentValue, CancellationToken cancellationToken = default);
    Task ValidateSubmissionAsync(FormDefinition form, Dictionary<string, object?> values, RequestType requestType, IReadOnlyDictionary<string, object?>? originalValues = null, CancellationToken cancellationToken = default);
    Task<LookupFilterPage> SearchFilterLevelAsync(FormDefinition form, FormField field, int level, IReadOnlyList<string> filters, string? parentValue, string? search, int offset = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupOption>> SearchFilteredAsync(FormDefinition form, FormField field, string? search, string? parentValue, IReadOnlyList<string> filters, CancellationToken cancellationToken = default);
    Task<LookupOption?> ResolveFilteredAsync(FormDefinition form, FormField field, string value, string? parentValue, IReadOnlyList<string> filters, CancellationToken cancellationToken = default);
    Task<LookupSelectionPath?> ResolvePathAsync(FormDefinition form, FormField field, string value, string? parentValue, bool enforceParent = true, CancellationToken cancellationToken = default);
}