using Dapper;
using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Repositories;
using System.Text.Json;

namespace Requestr.Core.Services.FormRequests;

/// <summary>
/// Implementation of form request history operations.
/// </summary>
public class FormRequestHistoryService : IFormRequestHistoryService
{
    private readonly IFormRequestHistoryRepository _historyRepository;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<FormRequestHistoryService> _logger;

    public FormRequestHistoryService(
        IFormRequestHistoryRepository historyRepository,
        IDbConnectionFactory connectionFactory,
        ILogger<FormRequestHistoryService> logger)
    {
        _historyRepository = historyRepository;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<List<FormRequestHistory>> GetHistoryAsync(int formRequestId)
    {
        try
        {
            return await _historyRepository.GetByFormRequestIdAsync(formRequestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting history for form request {FormRequestId}", formRequestId);
            throw;
        }
    }

    public async Task<FormRequestHistory> AddHistoryAsync(FormRequestHistory history)
    {
        try
        {
            var id = await _historyRepository.AddAsync(history);
            history.Id = id;
            return history;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding history entry for form request {FormRequestId}", history.FormRequestId);
            throw;
        }
    }

    public async Task RecordChangeAsync(
        int formRequestId,
        FormRequestChangeType changeType,
        Dictionary<string, object?>? previousValues,
        Dictionary<string, object?>? newValues,
        string changedBy,
        string changedByName,
        string? comments = null)
    {
        try
        {
            var history = new FormRequestHistory
            {
                FormRequestId = formRequestId,
                ChangeType = changeType,
                PreviousValues = previousValues ?? new Dictionary<string, object?>(),
                NewValues = newValues ?? new Dictionary<string, object?>(),
                ChangedBy = changedBy,
                ChangedByName = changedByName,
                ChangedAt = DateTime.UtcNow,
                Comments = comments
            };

            await _historyRepository.AddAsync(history);
            
            _logger.LogDebug("Recorded {ChangeType} change for form request {FormRequestId} by {ChangedBy}", 
                changeType, formRequestId, changedBy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording change for form request {FormRequestId}", formRequestId);
            throw;
        }
    }

    public async Task<string> GetDebugInfoAsync(int formRequestId)
    {
        try
        {
            var history = await GetHistoryAsync(formRequestId);
            var debugInfo = new System.Text.StringBuilder();
            
            foreach (var historyItem in history)
            {
                debugInfo.AppendLine($"History Item {historyItem.Id}: {historyItem.ChangeType}");
                debugInfo.AppendLine($"  PreviousValues JSON: {JsonSerializer.Serialize(historyItem.PreviousValues)}");
                debugInfo.AppendLine($"  NewValues JSON: {JsonSerializer.Serialize(historyItem.NewValues)}");
                debugInfo.AppendLine();
            }
            
            return debugInfo.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting debug info: {ex.Message}";
        }
    }
}
