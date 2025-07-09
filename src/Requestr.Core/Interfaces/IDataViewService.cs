using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;

namespace Requestr.Core.Interfaces;

public interface IDataViewService
{
    Task<DataViewResult> GetDataAsync(int formDefinitionId, int page = 1, int pageSize = 50, string? searchTerm = null, Dictionary<string, object?>? filters = null);
    Task<List<Dictionary<string, object?>>> GetSelectedRecordsAsync(int formDefinitionId, List<string> recordIds);
    Task<BulkFormRequest> CreateBulkUpdateRequestAsync(int formDefinitionId, List<Dictionary<string, object?>> records, Dictionary<string, object?> updates, string userId, string userName, string? comments = null);
    Task<BulkFormRequest> CreateBulkDeleteRequestAsync(int formDefinitionId, List<Dictionary<string, object?>> records, string userId, string userName, string? comments = null);
}

public class DataViewResult
{
    public List<Dictionary<string, object?>> Records { get; set; } = new();
    public int TotalCount { get; set; }
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<string> Columns { get; set; } = new();
    public List<string> PrimaryKeyColumns { get; set; } = new();
}
