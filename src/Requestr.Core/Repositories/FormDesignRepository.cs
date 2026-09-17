using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;

namespace Requestr.Core.Repositories;

public class FormDesignRepository(IDbConnectionFactory connectionFactory) : IFormDesignRepository
{
    public async Task<List<FormDefinition>> GetEditableFormsAsync(List<string> roles, bool isAdmin)
    {
        using var connection = await connectionFactory.CreateConnectionAsync();
        const string sql = """
            SELECT fd.Id, fd.Name, fd.Description, fd.Category, fd.IsActive
            FROM FormDefinitions fd
            WHERE fd.IsDeleted = 0 AND (@IsAdmin = 1 OR EXISTS (
                SELECT 1 FROM FormPermissions permission
                WHERE permission.FormDefinitionId = fd.Id AND permission.RoleName IN @Roles
                  AND permission.PermissionType = @PermissionType AND permission.IsGranted = 1))
            ORDER BY fd.Name, fd.Id
            """;
        var forms = await connection.QueryAsync<FormDefinition>(sql,
            new { Roles = roles, IsAdmin = isAdmin, PermissionType = (int)FormPermissionType.EditFormDesign });
        return forms.ToList();
    }

    public async Task<FormDefinition?> GetAsync(int formDefinitionId)
    {
        using var connection = await connectionFactory.CreateConnectionAsync();
        using var transaction = connection.BeginTransaction(IsolationLevel.RepeatableRead);
        var form = await ReadAsync(connection, transaction, formDefinitionId, false);
        await transaction.CommitAsync();
        return form;
    }

    private static async Task<FormDefinition?> ReadAsync(SqlConnection connection, SqlTransaction transaction, int formDefinitionId, bool forUpdate)
    {
        var locking = forUpdate ? "WITH (UPDLOCK, HOLDLOCK)" : "";
        var sql = $"""
            SELECT Id, Name, DesignVersion FROM FormDefinitions {locking}
            WHERE Id = @Id AND IsDeleted = 0;
            SELECT Id, FormDefinitionId, Name, DisplayName, DataType, ControlType, SqlDataType, MaxLength,
                   DropdownOptions, OptionSource, HelpText, DisplayOrder, FormSectionId, GridRow, GridColumn, GridColumnSpan
            FROM FormFields WHERE FormDefinitionId = @Id ORDER BY DisplayOrder, Id;
            SELECT Id, FormDefinitionId, Name, Description, DisplayOrder, MaxColumns, VisibilityCondition
            FROM FormSections WHERE FormDefinitionId = @Id ORDER BY DisplayOrder, Id;
            """;
        using var results = await connection.QueryMultipleAsync(sql, new { Id = formDefinitionId }, transaction);
        var form = await results.ReadSingleOrDefaultAsync<FormDefinition>();
        var fields = (await results.ReadAsync<FormField>()).ToList();
        var sections = (await results.ReadAsync<FormSection>()).ToList();
        if (form != null)
        {
            form.Fields = fields;
            form.Sections = sections;
        }
        return form;
    }

    public async Task SaveAsync(UpdateFormDesignDto update, List<string> roles, bool isAdmin, string changedBy)
    {
        using var connection = await connectionFactory.CreateConnectionAsync();
        using var transaction = connection.BeginTransaction();
        var current = await ReadAsync(connection, transaction, update.FormDefinitionId, true)
            ?? throw new InvalidOperationException("The form is no longer available.");

        if (!isAdmin)
        {
            const string permissionSql = """
                SELECT COUNT(*) FROM FormPermissions WITH (HOLDLOCK)
                WHERE FormDefinitionId = @Id AND RoleName IN @Roles
                  AND PermissionType = @PermissionType AND IsGranted = 1
                """;
            var granted = await connection.QuerySingleAsync<int>(permissionSql,
                new { Id = current.Id, Roles = roles, PermissionType = (int)FormPermissionType.EditFormDesign }, transaction);
            if (granted == 0)
                throw new UnauthorizedAccessException("You no longer have permission to edit this form.");
        }

        update.ValidateAgainst(current);
        var beforeJson = JsonSerializer.Serialize(UpdateFormDesignDto.FromForm(current));
        var sectionIds = new Dictionary<int, int>();
        foreach (var section in update.Sections)
        {
            if (current.Sections.Any(existing => existing.Id == section.Id))
            {
                const string sectionSql = """
                    UPDATE FormSections SET Name = @Name, Description = @Description, DisplayOrder = @DisplayOrder,
                        MaxColumns = @MaxColumns, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ChangedBy
                    WHERE Id = @Id AND FormDefinitionId = @FormDefinitionId
                    """;
                await connection.ExecuteAsync(sectionSql, new
                {
                    section.Id, section.Name, section.Description, section.DisplayOrder, section.MaxColumns,
                    FormDefinitionId = current.Id, ChangedBy = changedBy
                }, transaction);
                sectionIds[section.Id] = section.Id;
            }
            else
            {
                const string sectionSql = """
                    INSERT INTO FormSections (FormDefinitionId, Name, Description, DisplayOrder, MaxColumns, CreatedBy)
                    OUTPUT INSERTED.Id VALUES (@FormDefinitionId, @Name, @Description, @DisplayOrder, @MaxColumns, @ChangedBy)
                    """;
                sectionIds[section.Id] = await connection.QuerySingleAsync<int>(sectionSql, new
                {
                    FormDefinitionId = current.Id, section.Name, section.Description,
                    section.DisplayOrder, section.MaxColumns, ChangedBy = changedBy
                }, transaction);
            }
        }

        foreach (var field in update.Fields)
        {
            const string fieldSql = """
                UPDATE FormFields SET DisplayName = @DisplayName, HelpText = @HelpText, DropdownOptions = @DropdownOptions,
                    DisplayOrder = @DisplayOrder, FormSectionId = @FormSectionId,
                    GridRow = @GridRow, GridColumn = @GridColumn, GridColumnSpan = @GridColumnSpan
                WHERE Id = @Id AND FormDefinitionId = @FormDefinitionId
                """;
            await connection.ExecuteAsync(fieldSql, new
            {
                field.Id, field.DisplayName, field.HelpText, field.DropdownOptions, field.DisplayOrder,
                FormSectionId = field.FormSectionId.HasValue ? (int?)sectionIds[field.FormSectionId.Value] : null,
                field.GridRow, field.GridColumn, field.GridColumnSpan, FormDefinitionId = current.Id
            }, transaction);
        }

        foreach (var removed in current.Sections.Where(section => !sectionIds.ContainsKey(section.Id)))
        {
            await connection.ExecuteAsync("DELETE FROM FormSections WHERE Id = @Id AND FormDefinitionId = @FormDefinitionId",
                new { removed.Id, FormDefinitionId = current.Id }, transaction);
        }

        await connection.ExecuteAsync("""
            UPDATE FormDefinitions SET UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ChangedBy WHERE Id = @Id
            """, new { current.Id, ChangedBy = changedBy }, transaction);
        var saved = await ReadAsync(connection, transaction, current.Id, false);
        await connection.ExecuteAsync("""
            INSERT INTO FormDesignHistory (FormDefinitionId, ChangedBy, PreviousDesign, NewDesign)
            VALUES (@FormDefinitionId, @ChangedBy, @PreviousDesign, @NewDesign)
            """, new
        {
            FormDefinitionId = current.Id, ChangedBy = changedBy, PreviousDesign = beforeJson,
            NewDesign = JsonSerializer.Serialize(UpdateFormDesignDto.FromForm(saved!))
        }, transaction);
        await transaction.CommitAsync();
    }
}