using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;

namespace Requestr.Core.Services;

public class PermissionService : IPermissionService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PermissionService> _logger;
    private readonly string _connectionString;

    public PermissionService(IConfiguration configuration, ILogger<PermissionService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    #region Permission Management

    public async Task<List<ApplicationPermission>> GetPermissionsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = "SELECT * FROM ApplicationPermissions ORDER BY RoleName, Permission";
        var permissions = await connection.QueryAsync<ApplicationPermission>(sql);
        
        return permissions.ToList();
    }

    public async Task<List<ApplicationPermission>> GetPermissionsByRoleAsync(string roleName)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = "SELECT * FROM ApplicationPermissions WHERE RoleName = @RoleName ORDER BY Permission";
        var permissions = await connection.QueryAsync<ApplicationPermission>(sql, new { RoleName = roleName });
        
        return permissions.ToList();
    }

    public async Task<ApplicationPermission> GrantPermissionAsync(string roleName, string permission, int? resourceId = null)
    {
        using var connection = new SqlConnection(_connectionString);

        // Check if permission already exists
        const string checkSql = @"
            SELECT Id FROM ApplicationPermissions 
            WHERE RoleName = @RoleName AND Permission = @Permission 
            AND (@ResourceId IS NULL AND ResourceId IS NULL OR ResourceId = @ResourceId)";

        var existingId = await connection.QueryFirstOrDefaultAsync<int?>(checkSql, new 
        { 
            RoleName = roleName, 
            Permission = permission, 
            ResourceId = resourceId 
        });

        if (existingId.HasValue)
        {
            // Update existing permission to granted
            const string updateSql = "UPDATE ApplicationPermissions SET IsGranted = 1 WHERE Id = @Id";
            await connection.ExecuteAsync(updateSql, new { Id = existingId.Value });

            return new ApplicationPermission
            {
                Id = existingId.Value,
                RoleName = roleName,
                Permission = permission,
                ResourceId = resourceId,
                IsGranted = true,
                CreatedBy = "System"
            };
        }
        else
        {
            // Create new permission
            var newPermission = new ApplicationPermission
            {
                RoleName = roleName,
                Permission = permission,
                ResourceId = resourceId,
                IsGranted = true,
                CreatedBy = "System" // TODO: Get from current user context
            };

            const string insertSql = @"
                INSERT INTO ApplicationPermissions (RoleName, Permission, ResourceId, IsGranted, CreatedBy, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@RoleName, @Permission, @ResourceId, @IsGranted, @CreatedBy, @CreatedAt)";

            var id = await connection.QuerySingleAsync<int>(insertSql, newPermission);
            newPermission.Id = id;

            _logger.LogInformation("Granted permission {Permission} to role {RoleName}", permission, roleName);

            return newPermission;
        }
    }

    public async Task<bool> RevokePermissionAsync(string roleName, string permission, int? resourceId = null)
    {
        using var connection = new SqlConnection(_connectionString);

        const string sql = @"
            UPDATE ApplicationPermissions 
            SET IsGranted = 0 
            WHERE RoleName = @RoleName AND Permission = @Permission 
            AND (@ResourceId IS NULL AND ResourceId IS NULL OR ResourceId = @ResourceId)";

        var rowsAffected = await connection.ExecuteAsync(sql, new 
        { 
            RoleName = roleName, 
            Permission = permission, 
            ResourceId = resourceId 
        });

        if (rowsAffected > 0)
        {
            _logger.LogInformation("Revoked permission {Permission} from role {RoleName}", permission, roleName);
        }

        return rowsAffected > 0;
    }

    public async Task<bool> HasPermissionAsync(string roleName, string permission, int? resourceId = null)
    {
        using var connection = new SqlConnection(_connectionString);

        const string sql = @"
            SELECT CASE WHEN EXISTS(
                SELECT 1 FROM ApplicationPermissions 
                WHERE RoleName = @RoleName AND Permission = @Permission AND IsGranted = 1
                AND (@ResourceId IS NULL AND ResourceId IS NULL OR ResourceId = @ResourceId)
            ) THEN 1 ELSE 0 END";

        return await connection.QueryFirstOrDefaultAsync<bool>(sql, new 
        { 
            RoleName = roleName, 
            Permission = permission, 
            ResourceId = resourceId 
        });
    }

    public async Task<bool> HasPermissionAsync(List<string> userRoles, string permission, int? resourceId = null)
    {
        if (!userRoles.Any()) return false;

        using var connection = new SqlConnection(_connectionString);

        const string sql = @"
            SELECT CASE WHEN EXISTS(
                SELECT 1 FROM ApplicationPermissions 
                WHERE RoleName IN @UserRoles AND Permission = @Permission AND IsGranted = 1
                AND (@ResourceId IS NULL AND ResourceId IS NULL OR ResourceId = @ResourceId)
            ) THEN 1 ELSE 0 END";

        return await connection.QueryFirstOrDefaultAsync<bool>(sql, new 
        { 
            UserRoles = userRoles, 
            Permission = permission, 
            ResourceId = resourceId 
        });
    }

    #endregion

    #region Specific Permission Checks

    public async Task<bool> CanAccessDataViewAsync(List<string> userRoles)
    {
        return await HasPermissionAsync(userRoles, "DataView.Access");
    }

    public async Task<bool> CanExecuteBulkActionsAsync(List<string> userRoles)
    {
        return await HasPermissionAsync(userRoles, "BulkActions.Execute");
    }

    public async Task<bool> CanUploadCsvAsync(List<string> userRoles)
    {
        return await HasPermissionAsync(userRoles, "CSV.Upload");
    }

    public async Task<bool> CanDesignWorkflowsAsync(List<string> userRoles)
    {
        return await HasPermissionAsync(userRoles, "Workflow.Design");
    }

    public async Task<bool> CanManageWorkflowsAsync(List<string> userRoles)
    {
        return await HasPermissionAsync(userRoles, "Workflow.Manage");
    }

    #endregion

    #region Role-based Access

    public async Task<List<FormDefinition>> GetAccessibleFormsForDataViewAsync(List<string> userRoles)
    {
        if (!userRoles.Any()) return new List<FormDefinition>();

        using var connection = new SqlConnection(_connectionString);

        // Check if user has global DataView access
        var hasGlobalAccess = await CanAccessDataViewAsync(userRoles);
        if (hasGlobalAccess)
        {
            // Return all active forms
            const string allFormsSql = "SELECT * FROM FormDefinitions WHERE IsActive = 1 ORDER BY Name";
            var allForms = await connection.QueryAsync<FormDefinition>(allFormsSql);
            return allForms.ToList();
        }

        // Check for form-specific access
        const string formSpecificSql = @"
            SELECT DISTINCT fd.* FROM FormDefinitions fd
            INNER JOIN ApplicationPermissions ap ON ap.ResourceId = fd.Id
            WHERE ap.RoleName IN @UserRoles 
            AND ap.Permission = 'DataView.Access' 
            AND ap.IsGranted = 1
            AND fd.IsActive = 1
            ORDER BY fd.Name";

        var accessibleForms = await connection.QueryAsync<FormDefinition>(formSpecificSql, new { UserRoles = userRoles });
        return accessibleForms.ToList();
    }

    public async Task<bool> CanAccessFormDataViewAsync(List<string> userRoles, int formDefinitionId)
    {
        // Check global access first
        var hasGlobalAccess = await CanAccessDataViewAsync(userRoles);
        if (hasGlobalAccess) return true;

        // Check form-specific access
        return await HasPermissionAsync(userRoles, "DataView.Access", formDefinitionId);
    }

    #endregion
}
