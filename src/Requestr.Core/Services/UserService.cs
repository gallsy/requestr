using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;

namespace Requestr.Core.Services;

public class UserService : IUserService
{
    private readonly string _connectionString;
    private readonly ILogger<UserService> _logger;

    public UserService(IConfiguration configuration, ILogger<UserService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not found in configuration");
        _logger = logger;
    }

    public async Task<User> UpsertFromClaimsAsync(string userObjectId, string? tenantId, string? displayName, string? email, string? upn)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
MERGE dbo.Users AS target
USING (VALUES (@UserObjectId, @TenantId)) AS source(UserObjectId, TenantId)
    ON target.UserObjectId = source.UserObjectId AND
       ((target.TenantId IS NULL AND source.TenantId IS NULL) OR target.TenantId = source.TenantId)
WHEN MATCHED THEN 
    UPDATE SET DisplayName = COALESCE(@DisplayName, target.DisplayName),
               Email = COALESCE(@Email, target.Email),
               UPN = COALESCE(@UPN, target.UPN),
               UpdatedAt = SYSUTCDATETIME(),
               LastSeenAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (UserObjectId, TenantId, DisplayName, Email, UPN, CreatedAt, LastSeenAt)
    VALUES (@UserObjectId, @TenantId, @DisplayName, @Email, @UPN, SYSUTCDATETIME(), SYSUTCDATETIME())
OUTPUT inserted.*;";

        var user = await connection.QuerySingleAsync<User>(sql, new
        {
            UserObjectId = Guid.Parse(userObjectId),
            TenantId = string.IsNullOrWhiteSpace(tenantId) ? (Guid?)null : Guid.Parse(tenantId),
            DisplayName = displayName,
            Email = email,
            UPN = upn
        });

        return user;
    }

    public async Task<User?> GetByObjectIdAsync(string userObjectId, string? tenantId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
SELECT TOP 1 * FROM dbo.Users
WHERE UserObjectId = @UserObjectId AND ((TenantId IS NULL AND @TenantId IS NULL) OR TenantId = @TenantId)";

        return await connection.QueryFirstOrDefaultAsync<User>(sql, new
        {
            UserObjectId = Guid.Parse(userObjectId),
            TenantId = string.IsNullOrWhiteSpace(tenantId) ? (Guid?)null : Guid.Parse(tenantId)
        });
    }
}
