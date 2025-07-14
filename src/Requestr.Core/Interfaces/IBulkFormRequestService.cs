using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;

namespace Requestr.Core.Interfaces;

public interface IBulkFormRequestService
{
    Task<CsvUploadResult> ProcessCsvUploadAsync(int formDefinitionId, Stream csvStream, string fileName);
    Task<BulkFormRequest> CreateBulkFormRequestAsync(CreateBulkFormRequestDto createDto, string userId, string userName);
    Task<BulkFormRequest?> GetBulkFormRequestByIdAsync(int id);
    Task<List<BulkFormRequest>> GetBulkFormRequestsByUserAsync(string userId);
    Task<List<BulkFormRequest>> GetAllBulkFormRequestsAsync();
    Task<List<BulkFormRequest>> GetBulkFormRequestsForApprovalAsync(string userId, List<string> userRoles);
    Task<List<BulkFormRequest>> GetBulkFormRequestsByFormDefinitionIdAsync(int formDefinitionId, int limit = 10);
    Task<bool> ApproveBulkFormRequestAsync(int id, string userId, string userName, string? comments = null);
    Task<bool> RejectBulkFormRequestAsync(int id, string userId, string userName, string rejectionReason);
    Task<bool> DeleteBulkFormRequestAsync(int id, string userId);
}
