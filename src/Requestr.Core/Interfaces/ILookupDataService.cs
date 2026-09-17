using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

public record LookupOption(string Value, string Text);

public interface ILookupDataService
{
    Task ValidateConfigurationAsync(FormDefinition form, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupOption>> SearchAsync(FormDefinition form, FormField field, string? search, CancellationToken cancellationToken = default);
    Task<LookupOption?> ResolveAsync(FormDefinition form, FormField field, string value, CancellationToken cancellationToken = default);
    Task ValidateValuesAsync(FormDefinition form, Dictionary<string, object?> values, CancellationToken cancellationToken = default);
}