using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

public interface IConflictDetectionService
{
    Task<ConflictDetectionResult> CheckForConflictsAsync(FormRequest formRequest);
    Task<ConflictDetectionResult> CheckForConflictsAsync(int formRequestId);
    Task<List<ConflictDetectionResult>> CheckBulkRequestConflictsAsync(BulkFormRequest bulkRequest);
    Task<ConflictDetectionResult> CheckBulkRequestConflictsAsync(int bulkRequestId);
}

public class ConflictDetectionResult
{
    public bool HasConflicts { get; set; }
    public List<string> ConflictMessages { get; set; } = new();
    public int? FormRequestId { get; set; }
}
