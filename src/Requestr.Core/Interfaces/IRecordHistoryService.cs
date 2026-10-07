using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

public interface IRecordHistoryService
{
    /// <summary>
    /// Requests and bulk rows that targeted the record, across all forms on the same table, newest first.
    /// Callers must filter entries by the user's ViewRecordHistory permission on each entry's form.
    /// </summary>
    Task<List<RecordHistoryEntry>> GetHistoryAsync(FormDefinition form, string recordKey);
}
