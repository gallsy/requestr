using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;
using Requestr.Core.Repositories;
using Requestr.Core.Services;
using Xunit;

namespace Requestr.Core.Tests.Services;

public sealed class LocalDbFactAttribute : FactAttribute
{
    public LocalDbFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("REQUESTR_RUN_SQL_TESTS") != "1")
            Skip = "Set REQUESTR_RUN_SQL_TESTS=1 on Windows with MSSQLLocalDB to run isolated SQL tests.";
    }
}

public class FormDesignSqlTests : IAsyncLifetime
{
    private readonly string _databaseName = $"RequestrDelegationTest_{Guid.NewGuid():N}";
    private readonly string _masterConnection = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;Encrypt=false;Pooling=false";
    private string ConnectionString => new SqlConnectionStringBuilder(_masterConnection) { InitialCatalog = _databaseName }.ConnectionString;
    private FormDesignRepository _repository = default!;
    private bool _databaseCreated;

    public async Task InitializeAsync()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("REQUESTR_RUN_SQL_TESTS") != "1") return;
        using var master = new SqlConnection(_masterConnection);
        await master.OpenAsync();
        await master.ExecuteAsync($"CREATE DATABASE [{_databaseName}]");
        _databaseCreated = true;
        using var connection = await OpenAsync();
        await connection.ExecuteAsync("""
            CREATE TABLE FormDefinitions (
                Id int IDENTITY PRIMARY KEY, Name nvarchar(255) NOT NULL DEFAULT 'Countries', Description nvarchar(max) NULL,
                Category nvarchar(255) NULL, DatabaseConnectionName nvarchar(255) NOT NULL DEFAULT 'ReferenceData',
                TableName nvarchar(255) NOT NULL DEFAULT 'Countries', [Schema] nvarchar(255) NOT NULL DEFAULT 'dbo',
                ApproverRoles nvarchar(max) NULL, RequiresApproval bit NOT NULL DEFAULT 1,
                RequiresRequestComments bit NOT NULL DEFAULT 1, RequiresApprovalComments bit NOT NULL DEFAULT 1,
                IsActive bit NOT NULL DEFAULT 1, IsDeleted bit NOT NULL DEFAULT 0, WorkflowDefinitionId int NULL,
                NotificationEmail nvarchar(255) NULL, NotifyOnCreation bit NOT NULL DEFAULT 1, NotifyOnCompletion bit NOT NULL DEFAULT 1,
                CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), CreatedBy nvarchar(255) NOT NULL DEFAULT 'Admin',
                UpdatedAt datetime2 NULL, UpdatedBy nvarchar(255) NULL);
            CREATE TABLE FormSections (
                Id int IDENTITY PRIMARY KEY, FormDefinitionId int NOT NULL REFERENCES FormDefinitions(Id),
                Name nvarchar(255) NOT NULL, Description nvarchar(max) NULL, DisplayOrder int NOT NULL DEFAULT 0,
                MaxColumns int NOT NULL DEFAULT 12, VisibilityCondition nvarchar(max) NULL,
                IsCollapsible bit NOT NULL DEFAULT 0, DefaultExpanded bit NOT NULL DEFAULT 1,
                CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), CreatedBy nvarchar(255) NULL,
                UpdatedAt datetime2 NULL, UpdatedBy nvarchar(255) NULL);
            CREATE TABLE FormFields (
                Id int IDENTITY PRIMARY KEY, FormDefinitionId int NOT NULL REFERENCES FormDefinitions(Id),
                Name nvarchar(255) NOT NULL, DisplayName nvarchar(255) NOT NULL, DataType nvarchar(50) NOT NULL,
                ControlType nvarchar(50) NULL, SqlDataType nvarchar(50) NULL, MaxLength int NOT NULL DEFAULT 0,
                IsRequired bit NOT NULL DEFAULT 1, IsReadOnly bit NOT NULL DEFAULT 1, IsUnique bit NOT NULL DEFAULT 1,
                IsVisible bit NOT NULL DEFAULT 0, IsVisibleInDataView bit NOT NULL DEFAULT 0,
                DefaultValue nvarchar(max) NULL, ValidationRegex nvarchar(500) NULL, ValidationMessage nvarchar(500) NULL,
                VisibilityCondition nvarchar(500) NULL, TreatBlankAsNull bit NOT NULL DEFAULT 0,
                ComputedValueType int NULL, ComputedValueApplyMode int NOT NULL DEFAULT 0,
                DropdownOptions nvarchar(max) NULL, HelpText nvarchar(max) NULL, DisplayOrder int NOT NULL DEFAULT 0,
                FormSectionId int NULL REFERENCES FormSections(Id), GridRow int NOT NULL DEFAULT 1,
                GridColumn int NOT NULL DEFAULT 1, GridColumnSpan int NOT NULL DEFAULT 1);
            CREATE TABLE FormPermissions (
                FormDefinitionId int NOT NULL REFERENCES FormDefinitions(Id), RoleName nvarchar(255) NOT NULL,
                PermissionType int NOT NULL, IsGranted bit NOT NULL,
                UNIQUE(FormDefinitionId, RoleName, PermissionType));
            """);
        await ApplyMigrationAsync(connection);
        await ApplyMigrationAsync(connection);
        var factory = new Mock<IDbConnectionFactory>();
        factory.Setup(connectionFactory => connectionFactory.CreateConnectionAsync()).Returns(OpenAsync);
        _repository = new FormDesignRepository(factory.Object);
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ApplyMigrationAsync(SqlConnection connection)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "026_FormDesignDelegation.sql"));
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch)) await connection.ExecuteAsync(batch);
        }
    }

    private async Task<FormDefinition> CreateFormAsync(bool grant = true)
    {
        using var connection = await OpenAsync();
        var formId = await connection.QuerySingleAsync<int>("""
            INSERT INTO FormDefinitions (WorkflowDefinitionId, NotificationEmail, ApproverRoles)
            OUTPUT INSERTED.Id VALUES (7, 'owner@example.test', '["Approvers"]')
            """);
        var sectionId = await connection.QuerySingleAsync<int>("""
            INSERT INTO FormSections (FormDefinitionId, Name) OUTPUT INSERTED.Id VALUES (@FormId, 'Details')
            """, new { FormId = formId });
        await connection.ExecuteAsync("""
            INSERT INTO FormFields (FormDefinitionId, FormSectionId, Name, DisplayName, DataType, ControlType, SqlDataType,
                MaxLength, DefaultValue, ValidationRegex, VisibilityCondition, DropdownOptions)
            VALUES (@FormId, @SectionId, 'Region', 'Region', 'text', 'select', 'nvarchar', 50, 'North', '^.+$', 'Restricted', 'North');
            INSERT INTO FormPermissions VALUES (@FormId, 'Editors', 40, @Grant);
            """, new { FormId = formId, SectionId = sectionId, Grant = grant });
        return (await _repository.GetAsync(formId))!;
    }

    [LocalDbFact]
    public async Task SavePreservesProtectedValuesAndFieldIdentityAndWritesAudit()
    {
        var current = await CreateFormAsync();
        using var connection = await OpenAsync();
        var before = await ProtectedValuesAsync(connection, current.Id);
        var update = UpdateFormDesignDto.FromForm(current);
        update.Fields[0].DisplayName = "Region label";
        update.Fields[0].DropdownOptions = "North\nSouth";
        update.Fields[0].HelpText = "Choose a region";
        update.Sections[0].Name = "Location";
        await _repository.SaveAsync(update, new() { "Editors" }, false, "editor-id");
        Assert.Equal(before, await ProtectedValuesAsync(connection, current.Id));
        var saved = (await _repository.GetAsync(current.Id))!;
        Assert.Equal(current.Fields[0].Id, saved.Fields[0].Id);
        Assert.Equal("Region label", saved.Fields[0].DisplayName);
        Assert.Equal("North\nSouth", saved.Fields[0].DropdownOptions);
        Assert.Equal("Location", saved.Sections[0].Name);
        Assert.False(current.DesignVersion!.SequenceEqual(saved.DesignVersion!));
        Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM FormDesignHistory WHERE FormDefinitionId = @Id AND ChangedBy = 'editor-id'", new { current.Id }));
    }

    private static Task<string> ProtectedValuesAsync(SqlConnection connection, int formId) => connection.QuerySingleAsync<string>("""
        SELECT fd.DatabaseConnectionName, fd.TableName, fd.[Schema], fd.ApproverRoles, fd.WorkflowDefinitionId,
            fd.IsActive, fd.RequiresApproval, fd.RequiresRequestComments, fd.RequiresApprovalComments,
            fd.NotificationEmail, fd.NotifyOnCreation, fd.NotifyOnCompletion,
            ff.Name, ff.DataType, ff.ControlType, ff.SqlDataType, ff.MaxLength, ff.IsRequired, ff.IsReadOnly,
            ff.IsUnique, ff.IsVisible, ff.IsVisibleInDataView, ff.DefaultValue, ff.ValidationRegex,
            ff.VisibilityCondition, ff.TreatBlankAsNull, ff.ComputedValueType, ff.ComputedValueApplyMode
        FROM FormDefinitions fd JOIN FormFields ff ON ff.FormDefinitionId = fd.Id
        WHERE fd.Id = @FormId FOR JSON PATH, INCLUDE_NULL_VALUES
        """, new { FormId = formId });

    [LocalDbFact]
    public async Task RevokedPermissionPreventsDatabaseWrite()
    {
        var current = await CreateFormAsync();
        using var connection = await OpenAsync();
        await connection.ExecuteAsync("UPDATE FormPermissions SET IsGranted = 0 WHERE FormDefinitionId = @Id", new { current.Id });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _repository.SaveAsync(UpdateFormDesignDto.FromForm(current), new() { "Editors" }, false, "editor-id"));
        Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM FormDesignHistory"));
    }

    [LocalDbFact]
    public async Task ConcurrentEditorsCannotOverwriteOneAnother()
    {
        var current = await CreateFormAsync();
        var first = UpdateFormDesignDto.FromForm(current);
        var second = UpdateFormDesignDto.FromForm(current);
        first.Fields[0].DisplayName = "First";
        second.Fields[0].DisplayName = "Second";
        async Task<bool> SaveAsync(UpdateFormDesignDto update)
        {
            try
            {
                await _repository.SaveAsync(update, new() { "Editors" }, false, "editor-id");
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        var results = await Task.WhenAll(SaveAsync(first), SaveAsync(second));
        Assert.Single(results.Where(success => success));
        using var connection = await OpenAsync();
        Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM FormDesignHistory"));
    }

    [LocalDbFact]
    public async Task AuditFailureRollsBackPresentationChanges()
    {
        var current = await CreateFormAsync();
        using var connection = await OpenAsync();
        await connection.ExecuteAsync("ALTER TABLE FormDesignHistory ADD CONSTRAINT RejectTestAudit CHECK (ChangedBy <> 'reject-audit')");
        var update = UpdateFormDesignDto.FromForm(current);
        update.Fields[0].DisplayName = "Must roll back";
        await Assert.ThrowsAsync<SqlException>(() => _repository.SaveAsync(update, new() { "Editors" }, false, "reject-audit"));
        var saved = (await _repository.GetAsync(current.Id))!;
        Assert.Equal("Region", saved.Fields[0].DisplayName);
        Assert.True(current.DesignVersion!.SequenceEqual(saved.DesignVersion!));
    }

    [LocalDbFact]
    public async Task ListIncludesGrantedInactiveFormsButNotLegacyGrantsOrDeletedForms()
    {
        var granted = await CreateFormAsync();
        var legacy = await CreateFormAsync(false);
        var deleted = await CreateFormAsync();
        using var connection = await OpenAsync();
        await connection.ExecuteAsync("UPDATE FormDefinitions SET IsActive = 0 WHERE Id = @Id", new { granted.Id });
        await connection.ExecuteAsync("INSERT INTO FormPermissions VALUES (@Id, 'Editors', 32, 1)", new { legacy.Id });
        await connection.ExecuteAsync("UPDATE FormDefinitions SET IsDeleted = 1 WHERE Id = @Id", new { deleted.Id });
        var forms = await _repository.GetEditableFormsAsync(new() { "Editors" }, false);
        Assert.Equal(granted.Id, Assert.Single(forms).Id);
        Assert.False(forms[0].IsActive);
        Assert.Empty(await _repository.GetEditableFormsAsync(new(), false));
        Assert.Equal(2, (await _repository.GetEditableFormsAsync(new(), true)).Count);
    }

    [LocalDbFact]
    public async Task SectionReplacementKeepsFieldsAndProtectedSettings()
    {
        var current = await CreateFormAsync();
        var update = UpdateFormDesignDto.FromForm(current);
        update.Sections = new() { new() { Id = -1, Name = "New layout", MaxColumns = 2 } };
        update.Fields[0].FormSectionId = -1;
        using var connection = await OpenAsync();
        var before = await ProtectedValuesAsync(connection, current.Id);
        await _repository.SaveAsync(update, new() { "Editors" }, false, "editor-id");
        var saved = (await _repository.GetAsync(current.Id))!;
        Assert.Equal(current.Fields[0].Id, saved.Fields[0].Id);
        Assert.Equal(saved.Sections[0].Id, saved.Fields[0].FormSectionId);
        Assert.Equal("New layout", Assert.Single(saved.Sections).Name);
        Assert.Equal(before, await ProtectedValuesAsync(connection, current.Id));
    }

    [LocalDbFact]
    public async Task AdminSaveDetectsNewerDelegatedRevision()
    {
        var current = await CreateFormAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString
        }).Build();
        var definitions = new FormDefinitionService(configuration, NullLogger<FormDefinitionService>.Instance);
        var adminCopy = (await definitions.GetFormDefinitionAsync(current.Id))!;
        await _repository.SaveAsync(UpdateFormDesignDto.FromForm(current), new() { "Editors" }, false, "editor-id");
        await Assert.ThrowsAsync<InvalidOperationException>(() => definitions.UpdateFormDefinitionAsync(adminCopy));
        var fresh = (await definitions.GetFormDefinitionAsync(current.Id))!;
        fresh.Fields[0].DisplayName = "Admin change";
        await definitions.UpdateFormDefinitionAsync(fresh);
        var saved = (await definitions.GetFormDefinitionAsync(current.Id))!;
        Assert.Equal("Admin change", saved.Fields[0].DisplayName);
        Assert.True(fresh.DesignVersion!.SequenceEqual(saved.DesignVersion!));
    }

    public async Task DisposeAsync()
    {
        if (!_databaseCreated) return;
        using var master = new SqlConnection(_masterConnection);
        await master.OpenAsync();
        await master.ExecuteAsync($"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]");
    }
}