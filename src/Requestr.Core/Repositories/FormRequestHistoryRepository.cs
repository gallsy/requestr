using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Repositories.Queries;
using System.Text.Json;

namespace Requestr.Core.Repositories;

/// <summary>
/// Repository implementation for FormRequestHistory data access.
/// </summary>
public class FormRequestHistoryRepository : IFormRequestHistoryRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<FormRequestHistoryRepository> _logger;
    
    public FormRequestHistoryRepository(IDbConnectionFactory connectionFactory, ILogger<FormRequestHistoryRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }
    
    public async Task<List<FormRequestHistory>> GetByFormRequestIdAsync(int formRequestId)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var rows = await connection.QueryAsync(
                FormRequestHistoryQueries.GetByFormRequestId, 
                new { FormRequestId = formRequestId },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            return rows.Select(MapHistory).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting history for form request {FormRequestId}", formRequestId);
            throw;
        }
    }
    
    public async Task<int> AddAsync(FormRequestHistory history)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var id = await connection.QuerySingleAsync<int>(
                FormRequestHistoryQueries.Create,
                new
                {
                    history.FormRequestId,
                    ChangeType = (int)history.ChangeType,
                    PreviousValues = JsonSerializer.Serialize(history.PreviousValues),
                    NewValues = JsonSerializer.Serialize(history.NewValues),
                    history.ChangedBy,
                    history.ChangedAt,
                    history.Comments
                },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            _logger.LogDebug("Added history entry {Id} for form request {FormRequestId}", id, history.FormRequestId);
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding history for form request {FormRequestId}", history.FormRequestId);
            throw;
        }
    }
    
    public async Task<int> AddAsync(FormRequestHistory history, IDbConnection connection, IDbTransaction transaction)
    {
        var id = await connection.QuerySingleAsync<int>(
            FormRequestHistoryQueries.Create,
            new
            {
                history.FormRequestId,
                ChangeType = (int)history.ChangeType,
                PreviousValues = JsonSerializer.Serialize(history.PreviousValues),
                NewValues = JsonSerializer.Serialize(history.NewValues),
                history.ChangedBy,
                history.ChangedAt,
                history.Comments
            },
            transaction,
            commandTimeout: _connectionFactory.DefaultCommandTimeout);
        
        _logger.LogDebug("Added history entry {Id} for form request {FormRequestId} within transaction", id, history.FormRequestId);
        return id;
    }
    
    private FormRequestHistory MapHistory(dynamic row)
    {
        return new FormRequestHistory
        {
            Id = (int)row.Id,
            FormRequestId = (int)row.FormRequestId,
            ChangeType = (FormRequestChangeType)(int)row.ChangeType,
            PreviousValues = JsonSerializer.Deserialize<Dictionary<string, object?>>((string)(row.PreviousValuesJson ?? "{}")) ?? new Dictionary<string, object?>(),
            NewValues = JsonSerializer.Deserialize<Dictionary<string, object?>>((string)(row.NewValuesJson ?? "{}")) ?? new Dictionary<string, object?>(),
            ChangedBy = (string)row.ChangedBy,
            ChangedByName = (string)row.ChangedByName,
            ChangedAt = (DateTime)row.ChangedAt,
            Comments = (string?)row.Comments
        };
    }
}
