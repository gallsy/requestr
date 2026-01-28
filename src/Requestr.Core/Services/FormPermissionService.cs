using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;

namespace Requestr.Core.Services;

public class FormPermissionService : IFormPermissionService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<FormPermissionService> _logger;
    private readonly string _connectionString;

    public FormPermissionService(
        IConfiguration configuration, 
        ILogger<FormPermissionService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    public async Task<List<FormPermission>> GetFormPermissionsAsync(int formDefinitionId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            const string sql = @"
                SELECT Id, FormDefinitionId, RoleName, PermissionType, IsGranted, Conditions, 
                       CreatedAt, CreatedBy, UpdatedAt, UpdatedBy
                FROM FormPermissions 
                WHERE FormDefinitionId = @FormDefinitionId
                ORDER BY RoleName, PermissionType";
            
            var permissions = await connection.QueryAsync<FormPermission>(sql, new { FormDefinitionId = formDefinitionId });
            return permissions.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving form permissions for form {FormDefinitionId}", formDefinitionId);
            return new List<FormPermission>();
        }
    }

    public async Task<List<FormPermission>> GetFormPermissionsByRoleAsync(int formDefinitionId, string roleName)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            const string sql = @"
                SELECT Id, FormDefinitionId, RoleName, PermissionType, IsGranted, Conditions, 
                       CreatedAt, CreatedBy, UpdatedAt, UpdatedBy
                FROM FormPermissions 
                WHERE FormDefinitionId = @FormDefinitionId AND RoleName = @RoleName
                ORDER BY PermissionType";
            
            var permissions = await connection.QueryAsync<FormPermission>(sql, new { FormDefinitionId = formDefinitionId, RoleName = roleName });
            return permissions.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving form permissions for form {FormDefinitionId} and role {RoleName}", formDefinitionId, roleName);
            return new List<FormPermission>();
        }
    }

    public async Task<bool> HasPermissionAsync(int formDefinitionId, string roleName, FormPermissionType permissionType)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            const string sql = @"
                SELECT IsGranted 
                FROM FormPermissions 
                WHERE FormDefinitionId = @FormDefinitionId 
                  AND RoleName = @RoleName 
                  AND PermissionType = @PermissionType";
            
            var isGranted = await connection.QueryFirstOrDefaultAsync<bool?>(sql, new { 
                FormDefinitionId = formDefinitionId, 
                RoleName = roleName, 
                PermissionType = (int)permissionType 
            });
            
            return isGranted ?? false; // Default to false if permission not found
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking permission for form {FormDefinitionId}, role {RoleName}, permission {PermissionType}", 
                formDefinitionId, roleName, permissionType);
            return false;
        }
    }

    public async Task<Result> SetPermissionAsync(int formDefinitionId, string roleName, FormPermissionType permissionType, bool isGranted, string updatedBy)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            // Check if permission already exists
            const string checkSql = @"
                SELECT Id FROM FormPermissions 
                WHERE FormDefinitionId = @FormDefinitionId 
                  AND RoleName = @RoleName 
                  AND PermissionType = @PermissionType";
            
            var existingId = await connection.QueryFirstOrDefaultAsync<int?>(checkSql, new { 
                FormDefinitionId = formDefinitionId, 
                RoleName = roleName, 
                PermissionType = (int)permissionType 
            });
            
            if (existingId.HasValue)
            {
                // Update existing permission
                const string updateSql = @"
                    UPDATE FormPermissions 
                    SET IsGranted = @IsGranted, UpdatedAt = GETUTCDATE(), UpdatedBy = @UpdatedBy
                    WHERE Id = @Id";
                
                await connection.ExecuteAsync(updateSql, new { 
                    Id = existingId.Value, 
                    IsGranted = isGranted, 
                    UpdatedBy = updatedBy 
                });
            }
            else
            {
                // Insert new permission
                const string insertSql = @"
                    INSERT INTO FormPermissions (FormDefinitionId, RoleName, PermissionType, IsGranted, CreatedBy)
                    VALUES (@FormDefinitionId, @RoleName, @PermissionType, @IsGranted, @CreatedBy)";
                
                await connection.ExecuteAsync(insertSql, new { 
                    FormDefinitionId = formDefinitionId, 
                    RoleName = roleName, 
                    PermissionType = (int)permissionType, 
                    IsGranted = isGranted, 
                    CreatedBy = updatedBy 
                });
            }
            
            _logger.LogInformation("Permission {PermissionType} {Action} for role {RoleName} on form {FormDefinitionId}", 
                permissionType, isGranted ? "granted" : "revoked", roleName, formDefinitionId);
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting permission for form {FormDefinitionId}, role {RoleName}, permission {PermissionType}", 
                formDefinitionId, roleName, permissionType);
            return Result.Failure($"Failed to set permission: {ex.Message}");
        }
    }

    public async Task<Result> SetMultiplePermissionsAsync(int formDefinitionId, Dictionary<string, Dictionary<FormPermissionType, bool>> rolePermissions, string updatedBy)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();
            
            try
            {
                foreach (var roleEntry in rolePermissions)
                {
                    var roleName = roleEntry.Key;
                    var permissions = roleEntry.Value;
                    
                    foreach (var permissionEntry in permissions)
                    {
                        var permissionType = permissionEntry.Key;
                        var isGranted = permissionEntry.Value;
                        
                        // Check if permission already exists
                        const string checkSql = @"
                            SELECT Id FROM FormPermissions 
                            WHERE FormDefinitionId = @FormDefinitionId 
                              AND RoleName = @RoleName 
                              AND PermissionType = @PermissionType";
                        
                        var existingId = await connection.QueryFirstOrDefaultAsync<int?>(checkSql, new { 
                            FormDefinitionId = formDefinitionId, 
                            RoleName = roleName, 
                            PermissionType = (int)permissionType 
                        }, transaction);
                        
                        if (existingId.HasValue)
                        {
                            // Update existing permission
                            const string updateSql = @"
                                UPDATE FormPermissions 
                                SET IsGranted = @IsGranted, UpdatedAt = GETUTCDATE(), UpdatedBy = @UpdatedBy
                                WHERE Id = @Id";
                            
                            await connection.ExecuteAsync(updateSql, new { 
                                Id = existingId.Value, 
                                IsGranted = isGranted, 
                                UpdatedBy = updatedBy 
                            }, transaction);
                        }
                        else
                        {
                            // Insert new permission
                            const string insertSql = @"
                                INSERT INTO FormPermissions (FormDefinitionId, RoleName, PermissionType, IsGranted, CreatedBy)
                                VALUES (@FormDefinitionId, @RoleName, @PermissionType, @IsGranted, @CreatedBy)";
                            
                            await connection.ExecuteAsync(insertSql, new { 
                                FormDefinitionId = formDefinitionId, 
                                RoleName = roleName, 
                                PermissionType = (int)permissionType, 
                                IsGranted = isGranted, 
                                CreatedBy = updatedBy 
                            }, transaction);
                        }
                    }
                }
                
                await transaction.CommitAsync();
                
                _logger.LogInformation("Multiple permissions updated for form {FormDefinitionId}", formDefinitionId);
                return Result.Success();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting multiple permissions for form {FormDefinitionId}", formDefinitionId);
            return Result.Failure($"Failed to set multiple permissions: {ex.Message}");
        }
    }

    public async Task<List<string>> GetAvailableRolesAsync()
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            // Get all unique role names from the FormPermissions table
            const string sql = "SELECT DISTINCT RoleName FROM FormPermissions ORDER BY RoleName";
            var roles = await connection.QueryAsync<string>(sql);
            
            var roleList = roles.ToList();
            
            // If no roles exist yet, return some default ones to get started
            if (!roleList.Any())
            {
                roleList = new List<string>
                {
                    "Admin",
                    "User"
                };
            }
            
            return roleList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving available roles");
            
            // Return default roles on error
            return new List<string>
            {
                "Admin",
                "FormAdmin", 
                "DataAdmin",
                "User"
            };
        }
    }

    public async Task<List<string>> GetFormRolesAsync(int formDefinitionId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            const string sql = @"
                SELECT DISTINCT RoleName 
                FROM FormPermissions 
                WHERE FormDefinitionId = @FormDefinitionId 
                ORDER BY RoleName";
                
            var roles = await connection.QueryAsync<string>(sql, new { FormDefinitionId = formDefinitionId });
            return roles.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving roles for form {FormDefinitionId}", formDefinitionId);
            return new List<string>();
        }
    }

    public async Task<Result> AddRoleToFormAsync(int formDefinitionId, string roleName, string createdBy)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(roleName))
            {
                return Result.Failure("Role name cannot be empty");
            }

            using var connection = new SqlConnection(_connectionString);
            
            // Check if role already exists for this form
            const string checkSql = @"
                SELECT COUNT(*) FROM FormPermissions 
                WHERE FormDefinitionId = @FormDefinitionId AND RoleName = @RoleName";
                
            var existingCount = await connection.QuerySingleAsync<int>(checkSql, 
                new { FormDefinitionId = formDefinitionId, RoleName = roleName });
            
            if (existingCount > 0)
            {
                return Result.Failure($"Role '{roleName}' already exists for this form");
            }

            // Add the role with default permissions (all disabled initially)
            const string insertSql = @"
                INSERT INTO FormPermissions (FormDefinitionId, RoleName, PermissionType, IsGranted, CreatedBy)
                VALUES (@FormDefinitionId, @RoleName, @PermissionType, 0, @CreatedBy)";

            var insertData = new List<object>();
            foreach (var permissionType in Enum.GetValues<FormPermissionType>())
            {
                insertData.Add(new
                {
                    FormDefinitionId = formDefinitionId,
                    RoleName = roleName,
                    PermissionType = (int)permissionType,
                    CreatedBy = createdBy
                });
            }

            await connection.ExecuteAsync(insertSql, insertData);
            
            _logger.LogInformation("Added role '{RoleName}' to form {FormDefinitionId}", roleName, formDefinitionId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding role '{RoleName}' to form {FormDefinitionId}", roleName, formDefinitionId);
            return Result.Failure($"Failed to add role: {ex.Message}");
        }
    }

    public async Task<Result> RemoveRoleFromFormAsync(int formDefinitionId, string roleName)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            const string deleteSql = @"
                DELETE FROM FormPermissions 
                WHERE FormDefinitionId = @FormDefinitionId AND RoleName = @RoleName";
                
            var deletedCount = await connection.ExecuteAsync(deleteSql, 
                new { FormDefinitionId = formDefinitionId, RoleName = roleName });
            
            if (deletedCount == 0)
            {
                return Result.Failure($"Role '{roleName}' not found for this form");
            }
            
            _logger.LogInformation("Removed role '{RoleName}' from form {FormDefinitionId} ({DeletedCount} permissions deleted)", 
                roleName, formDefinitionId, deletedCount);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing role '{RoleName}' from form {FormDefinitionId}", roleName, formDefinitionId);
            return Result.Failure($"Failed to remove role: {ex.Message}");
        }
    }

    public async Task<Result> InitializeDefaultPermissionsAsync(int formDefinitionId, string createdBy)
    {
        try
        {
            var defaultPermissions = new Dictionary<string, Dictionary<FormPermissionType, bool>>
            {
                ["Admin"] = new()
                {
                    [FormPermissionType.CreateRequest] = true,
                    [FormPermissionType.UpdateRequest] = true,
                    [FormPermissionType.DeleteRequest] = true,
                    [FormPermissionType.ViewData] = true,
                    [FormPermissionType.BulkActions] = true,
                    [FormPermissionType.BulkUploadCsv] = true
                }
            };

            return await SetMultiplePermissionsAsync(formDefinitionId, defaultPermissions, createdBy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing default permissions for form {FormDefinitionId}", formDefinitionId);
            return Result.Failure($"Failed to initialize default permissions: {ex.Message}");
        }
    }

    public async Task<Result> DeleteFormPermissionsAsync(int formDefinitionId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            const string sql = "DELETE FROM FormPermissions WHERE FormDefinitionId = @FormDefinitionId";
            var rowsAffected = await connection.ExecuteAsync(sql, new { FormDefinitionId = formDefinitionId });
            
            _logger.LogInformation("Deleted {RowsAffected} permissions for form {FormDefinitionId}", rowsAffected, formDefinitionId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting form permissions for form {FormDefinitionId}", formDefinitionId);
            return Result.Failure($"Failed to delete form permissions: {ex.Message}");
        }
    }
}
