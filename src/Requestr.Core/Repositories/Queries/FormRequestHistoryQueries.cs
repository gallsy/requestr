namespace Requestr.Core.Repositories.Queries;

/// <summary>
/// SQL queries for FormRequestHistory operations.
/// </summary>
public static class FormRequestHistoryQueries
{
    /// <summary>
    /// Gets history entries for a form request.
    /// </summary>
    public const string GetByFormRequestId = @"
        SELECT h.Id, h.FormRequestId, h.ChangeType, h.PreviousValues as PreviousValuesJson, 
               h.NewValues as NewValuesJson, h.ChangedBy, COALESCE(u.DisplayName, h.ChangedBy) AS ChangedByName, 
               h.ChangedAt, h.Comments
        FROM FormRequestHistory h
        LEFT JOIN Users u ON TRY_CONVERT(uniqueidentifier, h.ChangedBy) = u.UserObjectId
        WHERE h.FormRequestId = @FormRequestId
        ORDER BY h.ChangedAt DESC";
    
    /// <summary>
    /// Creates a new history entry.
    /// </summary>
    public const string Create = @"
        INSERT INTO FormRequestHistory (FormRequestId, ChangeType, PreviousValues, NewValues, ChangedBy, ChangedAt, Comments)
        OUTPUT INSERTED.Id
        VALUES (@FormRequestId, @ChangeType, @PreviousValues, @NewValues, @ChangedBy, @ChangedAt, @Comments)";
}
