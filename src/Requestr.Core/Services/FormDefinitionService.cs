using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using System.Text.Json;

namespace Requestr.Core.Services;

public class FormDefinitionService : IFormDefinitionService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<FormDefinitionService> _logger;
    private readonly string _connectionString;

    public FormDefinitionService(IConfiguration configuration, ILogger<FormDefinitionService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("DefaultConnection not found in configuration");
    }

    public async Task<List<FormDefinition>> GetFormDefinitionsAsync()
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT fd.Id, fd.Name, fd.Description, fd.Category, fd.DatabaseConnectionName, fd.TableName, fd.[Schema], 
                       fd.ApproverRoles as ApproverRolesJson, fd.RequiresApproval, fd.IsActive, 
                       COALESCE(fd.NotificationEmail, '') as NotificationEmail, 
                       COALESCE(fd.NotifyOnCreation, 0) as NotifyOnCreation, 
                       COALESCE(fd.NotifyOnCompletion, 0) as NotifyOnCompletion,
                       fd.CreatedAt, fd.CreatedBy, fd.UpdatedAt, fd.UpdatedBy,
                       ff.Id as FieldId, ff.FormDefinitionId, ff.Name as FieldName, ff.DisplayName, ff.DataType, ff.ControlType, ff.MaxLength, 
                       ff.IsRequired, ff.IsReadOnly, ff.IsVisible, ff.IsVisibleInDataView, ff.DefaultValue, ff.ValidationRegex, ff.ValidationMessage, 
                       ff.VisibilityCondition, ff.DropdownOptions, ff.DisplayOrder
                FROM FormDefinitions fd
                LEFT JOIN FormFields ff ON fd.Id = ff.FormDefinitionId
                ORDER BY fd.Name, ff.DisplayOrder";

            var rows = await connection.QueryAsync(sql);
            var formDict = new Dictionary<int, FormDefinition>();

            foreach (var row in rows)
            {
                if (!formDict.ContainsKey((int)row.Id))
                {
                    var form = new FormDefinition
                    {
                        Id = (int)row.Id,
                        Name = (string)row.Name,
                        Description = (string)(row.Description ?? ""),
                        Category = (string)(row.Category ?? ""),
                        DatabaseConnectionName = (string)row.DatabaseConnectionName,
                        TableName = (string)row.TableName,
                        Schema = (string)row.Schema,
                        RequiresApproval = (bool)row.RequiresApproval,
                        IsActive = (bool)row.IsActive,
                        CreatedAt = (DateTime)row.CreatedAt,
                        CreatedBy = (string)row.CreatedBy,
                        UpdatedAt = (DateTime?)row.UpdatedAt,
                        UpdatedBy = (string?)row.UpdatedBy,
                        Fields = new List<FormField>(),
                        ApproverRoles = JsonSerializer.Deserialize<List<string>>((string)(row.ApproverRolesJson ?? "[]")) ?? new List<string>()
                    };
                    formDict[(int)row.Id] = form;
                }

                if (row.FieldId != null)
                {
                    var field = new FormField
                    {
                        Id = (int)row.FieldId,
                        FormDefinitionId = (int)row.FormDefinitionId,
                        Name = (string)row.FieldName,
                        DisplayName = (string)row.DisplayName,
                        DataType = (string)row.DataType,
                        ControlType = row.ControlType?.ToString() ?? string.Empty,
                        MaxLength = (int)row.MaxLength,
                        IsRequired = (bool)row.IsRequired,
                        IsReadOnly = (bool)row.IsReadOnly,
                        IsVisible = (bool)row.IsVisible,
                        IsVisibleInDataView = (bool)row.IsVisibleInDataView,
                        DefaultValue = (string?)row.DefaultValue,
                        ValidationRegex = (string?)row.ValidationRegex,
                        ValidationMessage = (string?)row.ValidationMessage,
                        VisibilityCondition = (string?)row.VisibilityCondition,
                        DropdownOptions = (string?)row.DropdownOptions,
                        DisplayOrder = (int)row.DisplayOrder
                    };
                    formDict[(int)row.Id].Fields.Add(field);
                }
            }

            return formDict.Values.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form definitions");
            throw;
        }
    }

    public async Task<List<FormDefinition>> GetFormDefinitionsForUserAsync(string userId, List<string> userRoles)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT fd.Id, fd.Name, fd.Description, fd.DatabaseConnectionName, fd.TableName, fd.[Schema], 
                       fd.ApproverRoles as ApproverRolesJson, fd.RequiresApproval, fd.IsActive, 
                       COALESCE(fd.NotificationEmail, '') as NotificationEmail, 
                       COALESCE(fd.NotifyOnCreation, 0) as NotifyOnCreation, 
                       COALESCE(fd.NotifyOnCompletion, 0) as NotifyOnCompletion,
                       fd.CreatedAt, fd.CreatedBy, fd.UpdatedAt, fd.UpdatedBy,
                       ff.Id as FieldId, ff.FormDefinitionId, ff.Name as FieldName, ff.DisplayName, ff.DataType, ff.ControlType, ff.MaxLength, 
                       ff.IsRequired, ff.IsReadOnly, ff.IsVisible, ff.IsVisibleInDataView, ff.DefaultValue, ff.ValidationRegex, ff.ValidationMessage, 
                       ff.VisibilityCondition, ff.DropdownOptions, ff.DisplayOrder
                FROM FormDefinitions fd
                LEFT JOIN FormFields ff ON fd.Id = ff.FormDefinitionId
                WHERE fd.IsActive = 1
                ORDER BY fd.Name, ff.DisplayOrder";

            var rows = await connection.QueryAsync(sql);
            var formDict = new Dictionary<int, FormDefinition>();

            foreach (var row in rows)
            {
                if (!formDict.ContainsKey((int)row.Id))
                {
                    var form = new FormDefinition
                    {
                        Id = (int)row.Id,
                        Name = (string)row.Name,
                        Description = (string)(row.Description ?? ""),
                        DatabaseConnectionName = (string)row.DatabaseConnectionName,
                        TableName = (string)row.TableName,
                        Schema = (string)(row.Schema ?? "dbo"),
                        ApproverRoles = JsonSerializer.Deserialize<List<string>>((string)(row.ApproverRolesJson ?? "[]")) ?? new List<string>(),
                        RequiresApproval = (bool)row.RequiresApproval,
                        IsActive = (bool)row.IsActive,
                        CreatedAt = (DateTime)row.CreatedAt,
                        CreatedBy = (string)(row.CreatedBy ?? ""),
                        UpdatedAt = row.UpdatedAt as DateTime?,
                        UpdatedBy = row.UpdatedBy as string
                    };

                    formDict[form.Id] = form;
                }

                if (row.FieldId != null)
                {
                    var field = new FormField
                    {
                        Id = (int)row.FieldId,
                        FormDefinitionId = (int)row.FormDefinitionId,
                        Name = (string)row.FieldName,
                        DisplayName = (string)(row.DisplayName ?? ""),
                        DataType = (string)row.DataType,
                        ControlType = (row.ControlType as string) ?? string.Empty,
                        MaxLength = (int)(row.MaxLength ?? 0),
                        IsRequired = (bool)(row.IsRequired ?? false),
                        IsReadOnly = (bool)(row.IsReadOnly ?? false),
                        IsVisible = (bool)(row.IsVisible ?? true),
                        IsVisibleInDataView = (bool)(row.IsVisibleInDataView ?? true),
                        DefaultValue = row.DefaultValue as string,
                        ValidationRegex = row.ValidationRegex as string,
                        ValidationMessage = row.ValidationMessage as string,
                        VisibilityCondition = row.VisibilityCondition as string,
                        DropdownOptions = row.DropdownOptions as string,
                        DisplayOrder = (int)(row.DisplayOrder ?? 0)
                    };

                    formDict[(int)row.FormDefinitionId].Fields.Add(field);
                }
            }

            return formDict.Values.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form definitions for user {UserId}", userId);
            throw;
        }
    }

    public async Task<FormDefinition?> GetFormDefinitionAsync(int id)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT fd.Id, fd.Name, fd.Description, fd.Category, fd.DatabaseConnectionName, fd.TableName, fd.[Schema], 
                       fd.ApproverRoles as ApproverRolesJson, fd.RequiresApproval, fd.IsActive, fd.WorkflowDefinitionId, 
                       COALESCE(fd.NotificationEmail, '') as NotificationEmail, 
                       COALESCE(fd.NotifyOnCreation, 0) as NotifyOnCreation, 
                       COALESCE(fd.NotifyOnCompletion, 0) as NotifyOnCompletion,
                       fd.CreatedAt, fd.CreatedBy, fd.UpdatedAt, fd.UpdatedBy,
                       ff.Id as FieldId, ff.FormDefinitionId, ff.Name as FieldName, ff.DisplayName, ff.DataType, ff.ControlType, ff.MaxLength, 
                       ff.IsRequired, ff.IsReadOnly, ff.IsVisible, ff.IsVisibleInDataView, ff.DefaultValue, ff.ValidationRegex, ff.ValidationMessage, 
                       ff.VisibilityCondition, ff.DropdownOptions, ff.DisplayOrder
                FROM FormDefinitions fd
                LEFT JOIN FormFields ff ON fd.Id = ff.FormDefinitionId
                WHERE fd.Id = @Id AND fd.IsActive = 1
                ORDER BY ff.DisplayOrder";

            var rows = await connection.QueryAsync(sql, new { Id = id });
            if (!rows.Any()) return null;

            FormDefinition? form = null;

            foreach (var row in rows)
            {
                if (form == null)
                {
                    form = new FormDefinition
                    {
                        Id = (int)row.Id,
                        Name = (string)row.Name,
                        Description = (string)(row.Description ?? ""),
                        Category = (string)(row.Category ?? ""),
                        DatabaseConnectionName = (string)row.DatabaseConnectionName,
                        TableName = (string)row.TableName,
                        Schema = (string)row.Schema,
                        RequiresApproval = (bool)row.RequiresApproval,
                        IsActive = (bool)row.IsActive,
                        WorkflowDefinitionId = (int?)row.WorkflowDefinitionId,
                        NotificationEmail = (string?)row.NotificationEmail,
                        NotifyOnCreation = Convert.ToBoolean(row.NotifyOnCreation),
                        NotifyOnCompletion = Convert.ToBoolean(row.NotifyOnCompletion),
                        CreatedAt = (DateTime)row.CreatedAt,
                        CreatedBy = (string)row.CreatedBy,
                        UpdatedAt = (DateTime?)row.UpdatedAt,
                        UpdatedBy = (string?)row.UpdatedBy,
                        Fields = new List<FormField>(),
                        ApproverRoles = JsonSerializer.Deserialize<List<string>>((string)(row.ApproverRolesJson ?? "[]")) ?? new List<string>()
                    };
                }

                if (row.FieldId != null)
                {
                    var field = new FormField
                    {
                        Id = (int)row.FieldId,
                        FormDefinitionId = (int)row.FormDefinitionId,
                        Name = (string)row.FieldName,
                        DisplayName = (string)row.DisplayName,
                        DataType = (string)row.DataType,
                        ControlType = row.ControlType?.ToString() ?? string.Empty,
                        MaxLength = (int)row.MaxLength,
                        IsRequired = (bool)row.IsRequired,
                        IsReadOnly = (bool)row.IsReadOnly,
                        IsVisible = (bool)row.IsVisible,
                        IsVisibleInDataView = (bool)row.IsVisibleInDataView,
                        DefaultValue = (string?)row.DefaultValue,
                        ValidationRegex = (string?)row.ValidationRegex,
                        ValidationMessage = (string?)row.ValidationMessage,
                        VisibilityCondition = (string?)row.VisibilityCondition,
                        DropdownOptions = (string?)row.DropdownOptions,
                        DisplayOrder = (int)row.DisplayOrder
                    };
                    form.Fields.Add(field);
                }
            }

            return form;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form definition {Id}", id);
            throw;
        }
    }

    public async Task<FormDefinition> CreateFormDefinitionAsync(FormDefinition formDefinition)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var formSql = @"
                    INSERT INTO FormDefinitions (Name, Description, Category, DatabaseConnectionName, TableName, [Schema], ApproverRoles, RequiresApproval, IsActive, NotificationEmail, NotifyOnCreation, NotifyOnCompletion, CreatedAt, CreatedBy)
                    OUTPUT INSERTED.Id
                    VALUES (@Name, @Description, @Category, @DatabaseConnectionName, @TableName, @Schema, @ApproverRoles, @RequiresApproval, @IsActive, @NotificationEmail, @NotifyOnCreation, @NotifyOnCompletion, @CreatedAt, @CreatedBy)";

                var formId = await connection.QuerySingleAsync<int>(formSql, new
                {
                    formDefinition.Name,
                    formDefinition.Description,
                    formDefinition.Category,
                    formDefinition.DatabaseConnectionName,
                    formDefinition.TableName,
                    formDefinition.Schema,
                    ApproverRoles = JsonSerializer.Serialize(formDefinition.ApproverRoles),
                    formDefinition.RequiresApproval,
                    formDefinition.IsActive,
                    formDefinition.NotificationEmail,
                    formDefinition.NotifyOnCreation,
                    formDefinition.NotifyOnCompletion,
                    formDefinition.CreatedAt,
                    formDefinition.CreatedBy
                }, transaction);

                formDefinition.Id = formId;

                if (formDefinition.Fields.Any())
                {
                    var fieldSql = @"
                        INSERT INTO FormFields (FormDefinitionId, Name, DisplayName, DataType, ControlType, MaxLength, IsRequired, IsReadOnly, IsVisible, IsVisibleInDataView, DefaultValue, ValidationRegex, ValidationMessage, VisibilityCondition, DropdownOptions, DisplayOrder)
                        VALUES (@FormDefinitionId, @Name, @DisplayName, @DataType, @ControlType, @MaxLength, @IsRequired, @IsReadOnly, @IsVisible, @IsVisibleInDataView, @DefaultValue, @ValidationRegex, @ValidationMessage, @VisibilityCondition, @DropdownOptions, @DisplayOrder)";

                    foreach (var field in formDefinition.Fields)
                    {
                        field.FormDefinitionId = formId;
                        await connection.ExecuteAsync(fieldSql, field, transaction);
                    }
                }

                await transaction.CommitAsync();
                return formDefinition;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating form definition");
            throw;
        }
    }

    public async Task<FormDefinition> UpdateFormDefinitionAsync(FormDefinition formDefinition)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var formSql = @"
                    UPDATE FormDefinitions 
                    SET Name = @Name, Description = @Description, Category = @Category, DatabaseConnectionName = @DatabaseConnectionName, 
                        TableName = @TableName, [Schema] = @Schema, ApproverRoles = @ApproverRoles, 
                        RequiresApproval = @RequiresApproval, IsActive = @IsActive, 
                        NotificationEmail = @NotificationEmail, NotifyOnCreation = @NotifyOnCreation, NotifyOnCompletion = @NotifyOnCompletion,
                        WorkflowDefinitionId = @WorkflowDefinitionId,
                        UpdatedAt = @UpdatedAt, UpdatedBy = @UpdatedBy
                    WHERE Id = @Id";

                await connection.ExecuteAsync(formSql, new
                {
                    formDefinition.Id,
                    formDefinition.Name,
                    formDefinition.Description,
                    formDefinition.Category,
                    formDefinition.DatabaseConnectionName,
                    formDefinition.TableName,
                    formDefinition.Schema,
                    ApproverRoles = JsonSerializer.Serialize(formDefinition.ApproverRoles),
                    formDefinition.RequiresApproval,
                    formDefinition.IsActive,
                    formDefinition.NotificationEmail,
                    formDefinition.NotifyOnCreation,
                    formDefinition.NotifyOnCompletion,
                    formDefinition.WorkflowDefinitionId,
                    formDefinition.UpdatedAt,
                    formDefinition.UpdatedBy
                }, transaction);

                // Delete existing fields and recreate them
                await connection.ExecuteAsync("DELETE FROM FormFields WHERE FormDefinitionId = @Id", 
                    new { formDefinition.Id }, transaction);

                if (formDefinition.Fields.Any())
                {
                    var fieldSql = @"
                        INSERT INTO FormFields (FormDefinitionId, Name, DisplayName, DataType, ControlType, MaxLength, IsRequired, IsReadOnly, IsVisible, IsVisibleInDataView, DefaultValue, ValidationRegex, ValidationMessage, VisibilityCondition, DropdownOptions, DisplayOrder)
                        VALUES (@FormDefinitionId, @Name, @DisplayName, @DataType, @ControlType, @MaxLength, @IsRequired, @IsReadOnly, @IsVisible, @IsVisibleInDataView, @DefaultValue, @ValidationRegex, @ValidationMessage, @VisibilityCondition, @DropdownOptions, @DisplayOrder)";

                    foreach (var field in formDefinition.Fields)
                    {
                        field.FormDefinitionId = formDefinition.Id;
                        await connection.ExecuteAsync(fieldSql, field, transaction);
                    }
                }

                // Update the workflow definition to establish bidirectional relationship
                if (formDefinition.WorkflowDefinitionId.HasValue)
                {
                    var workflowUpdateSql = @"
                        UPDATE WorkflowDefinitions 
                        SET FormDefinitionId = @FormDefinitionId, UpdatedAt = @UpdatedAt, UpdatedBy = @UpdatedBy
                        WHERE Id = @WorkflowDefinitionId";

                    await connection.ExecuteAsync(workflowUpdateSql, new
                    {
                        FormDefinitionId = formDefinition.Id,
                        WorkflowDefinitionId = formDefinition.WorkflowDefinitionId.Value,
                        formDefinition.UpdatedAt,
                        formDefinition.UpdatedBy
                    }, transaction);
                }

                await transaction.CommitAsync();
                return formDefinition;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating form definition {Id}", formDefinition.Id);
            throw;
        }
    }

    public async Task<bool> DeleteFormDefinitionAsync(int id)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "UPDATE FormDefinitions SET IsActive = 0 WHERE Id = @Id";
            var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting form definition {Id}", id);
            throw;
        }
    }

    public async Task<List<FormDefinition>> GetActiveAsync()
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT fd.Id, fd.Name, fd.Description, fd.Category, fd.DatabaseConnectionName, fd.TableName, fd.[Schema], 
                       fd.ApproverRoles as ApproverRolesJson, fd.RequiresApproval, fd.IsActive, fd.CreatedAt, fd.CreatedBy, fd.UpdatedAt, fd.UpdatedBy,
                       ff.Id as FieldId, ff.FormDefinitionId, ff.Name as FieldName, ff.DisplayName, ff.DataType, ff.ControlType, ff.MaxLength, 
                       ff.IsRequired, ff.IsReadOnly, ff.IsVisible, ff.IsVisibleInDataView, ff.DefaultValue, ff.ValidationRegex, ff.ValidationMessage, 
                       ff.VisibilityCondition, ff.DropdownOptions, ff.DisplayOrder
                FROM FormDefinitions fd
                LEFT JOIN FormFields ff ON fd.Id = ff.FormDefinitionId
                WHERE fd.IsActive = 1
                ORDER BY fd.Name, ff.DisplayOrder";

            var rows = await connection.QueryAsync(sql);
            var formDict = new Dictionary<int, FormDefinition>();

            foreach (var row in rows)
            {
                if (!formDict.ContainsKey((int)row.Id))
                {
                    var form = new FormDefinition
                    {
                        Id = (int)row.Id,
                        Name = (string)row.Name,
                        Description = (string)(row.Description ?? ""),
                        Category = (string)(row.Category ?? ""),
                        DatabaseConnectionName = (string)row.DatabaseConnectionName,
                        TableName = (string)row.TableName,
                        Schema = (string)row.Schema,
                        RequiresApproval = (bool)row.RequiresApproval,
                        IsActive = (bool)row.IsActive,
                        CreatedAt = (DateTime)row.CreatedAt,
                        CreatedBy = (string)row.CreatedBy,
                        UpdatedAt = (DateTime?)row.UpdatedAt,
                        UpdatedBy = (string?)row.UpdatedBy,
                        Fields = new List<FormField>(),
                        ApproverRoles = JsonSerializer.Deserialize<List<string>>((string)(row.ApproverRolesJson ?? "[]")) ?? new List<string>()
                    };
                    formDict[(int)row.Id] = form;
                }

                if (row.FieldId != null)
                {
                    var field = new FormField
                    {
                        Id = (int)row.FieldId,
                        FormDefinitionId = (int)row.FormDefinitionId,
                        Name = (string)row.FieldName,
                        DisplayName = (string)row.DisplayName,
                        DataType = (string)row.DataType,
                        ControlType = row.ControlType?.ToString() ?? string.Empty,
                        MaxLength = (int)row.MaxLength,
                        IsRequired = (bool)row.IsRequired,
                        IsReadOnly = (bool)row.IsReadOnly,
                        IsVisible = (bool)row.IsVisible,
                        IsVisibleInDataView = (bool)row.IsVisibleInDataView,
                        DefaultValue = (string?)row.DefaultValue,
                        ValidationRegex = (string?)row.ValidationRegex,
                        ValidationMessage = (string?)row.ValidationMessage,
                        VisibilityCondition = (string?)row.VisibilityCondition,
                        DropdownOptions = (string?)row.DropdownOptions,
                        DisplayOrder = (int)row.DisplayOrder
                    };
                    formDict[(int)row.Id].Fields.Add(field);
                }
            }

            return formDict.Values.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active form definitions");
            throw;
        }
    }

    // Alias methods for UI compatibility
    public async Task<List<FormDefinition>> GetAllAsync()
    {
        return await GetFormDefinitionsAsync();
    }
    
    public async Task<FormDefinition?> GetByIdAsync(int id)
    {
        return await GetFormDefinitionAsync(id);
    }
    
    public async Task<FormDefinition> CreateAsync(FormDefinition formDefinition)
    {
        return await CreateFormDefinitionAsync(formDefinition);
    }
    
    public async Task<FormDefinition> UpdateAsync(FormDefinition formDefinition)
    {
        return await UpdateFormDefinitionAsync(formDefinition);
    }
    
    public async Task<bool> DeleteAsync(int id)
    {
        return await DeleteFormDefinitionAsync(id);
    }
}
