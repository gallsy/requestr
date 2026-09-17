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
using Requestr.Core.Interfaces;
using Requestr.Core.Services.FormRequests;
using Requestr.Core.Services.Workflow;
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
        var lookupMigration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "027_FormFieldLookups.sql"));
        await connection.ExecuteAsync(lookupMigration);
        await connection.ExecuteAsync(lookupMigration);
        var lookupConnectionMigration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "028_LookupDatabaseConnections.sql"));
        await connection.ExecuteAsync(lookupConnectionMigration);
        await connection.ExecuteAsync(lookupConnectionMigration);
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
        var definitions = new FormDefinitionService(configuration, NullLogger<FormDefinitionService>.Instance, new LookupDataService(configuration));
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

    [LocalDbFact]
    public async Task DataViewSearchMatchesVisibleNumbersAndBooleans()
    {
        using var connection = await OpenAsync();
        await connection.ExecuteAsync("""
            CREATE TABLE SearchRecords (Id int PRIMARY KEY, Name nvarchar(100), Amount decimal(12,2), Enabled bit NULL, HiddenNumber int);
            INSERT INTO SearchRecords VALUES (21, 'North office', 1250.75, 1, 998877), (32, 'South office', -42.50, 0, 998877), (43, 'Empty office', NULL, NULL, 998877);
            """);
        var form = new FormDefinition
        {
            DatabaseConnectionName = "ReferenceData", Schema = "dbo", TableName = "SearchRecords",
            Fields = new()
            {
                new() { Name = "Id", DataType = "int" },
                new() { Name = "Name", DataType = "text", SqlDataType = "nvarchar" },
                new() { Name = "Amount", DataType = "number", SqlDataType = "decimal" },
                new() { Name = "Enabled", DataType = "boolean", SqlDataType = "bit" },
                new() { Name = "HiddenNumber", DataType = "int", IsVisibleInDataView = false }
            }
        };
        var definitions = new Mock<IFormDefinitionService>();
        definitions.Setup(service => service.GetFormDefinitionAsync(1)).ReturnsAsync(form);
        var data = new Mock<IDataService>();
        data.Setup(service => service.GetPrimaryKeyColumnsAsync("ReferenceData", "SearchRecords", "dbo")).ReturnsAsync(new List<string> { "Id" });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString,
            ["DatabaseConnections:ReferenceData"] = ConnectionString
        }).Build();
        var service = new DataViewService(configuration, NullLogger<DataViewService>.Instance, definitions.Object, data.Object, Mock.Of<IBulkFormRequestService>());
        foreach (var (search, expectedId) in new[] { ("21", 21), ("1250.75", 21), ("-42.50", 32), ("true", 21), ("False", 32), ("North true", 21), ("\"North office\" 1250", 21) })
        {
            var result = await service.GetDataAsync(1, pageSize: 1, searchTerm: search);
            Assert.Equal(expectedId, Assert.Single(result.Records)["Id"]);
            Assert.Equal(1, result.TotalCount);
        }
        Assert.Empty((await service.GetDataAsync(1, searchTerm: "998877")).Records);
        Assert.Empty((await service.GetDataAsync(1, searchTerm: "North false")).Records);
        form.Fields.RemoveAll(field => field.Name == "Name");
        Assert.Equal(1, (await service.GetDataAsync(1, searchTerm: "true")).TotalCount);
        Assert.Empty((await service.GetDataAsync(1, searchTerm: "no-match")).Records);
        form.Fields.RemoveAll(field => field.Name != "Enabled");
        foreach (var (search, expectedValue) in new[] { ("True", true), ("False", false), ("1", true), ("0", false) })
        {
            var result = await service.GetDataAsync(1, searchTerm: search);
            Assert.Equal(expectedValue, Assert.Single(result.Records)["Enabled"]);
            Assert.Equal(1, result.TotalCount);
        }
    }

    private async Task<(LookupDataService Service, FormDefinition Form)> CreateLookupAsync(string keyType = "int")
    {
        using var connection = await OpenAsync();
        await connection.ExecuteAsync($"""
            CREATE TABLE LookupCountries (Id {keyType} NOT NULL PRIMARY KEY, Name nvarchar(100) NULL);
            CREATE TABLE LookupDestination (CountryId {keyType} NULL);
            """);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString,
            ["ConnectionStrings:ReferenceData"] = ConnectionString
        }).Build();
        return (new LookupDataService(configuration), new FormDefinition
        {
            DatabaseConnectionName = "ReferenceData", TableName = "LookupDestination", Schema = "dbo",
            Fields = new() { new()
            {
                Name = "CountryId", DisplayName = "Country", ControlType = "searchable-select", SqlDataType = keyType,
                OptionSource = FieldOptionSource.DatabaseLookup, LookupSchema = "dbo", LookupTable = "LookupCountries",
                LookupKeyColumn = "Id", LookupLabelColumn = "Name", IsRequired = true
            } }
        });
    }

    [LocalDbFact]
    public async Task LookupUsesSeparateSourceConnectionAndValidatesActualDestination()
    {
        var (_, form) = await CreateLookupAsync();
        var referenceDatabase = new FormDesignSqlTests();
        await referenceDatabase.InitializeAsync();
        try
        {
            await referenceDatabase.CreateLookupAsync();
            using var source = await referenceDatabase.OpenAsync();
            await source.ExecuteAsync("INSERT INTO LookupCountries VALUES (12, 'Australia'); DROP TABLE LookupDestination;");
            using var destination = await OpenAsync();
            await destination.ExecuteAsync("INSERT INTO LookupCountries VALUES (12, 'Wrong database');");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ReferenceData"] = ConnectionString,
                ["ConnectionStrings:ExternalReferenceData"] = ConnectionString,
                ["DatabaseConnections:ExternalReferenceData"] = referenceDatabase.ConnectionString
            }).Build();
            var service = new LookupDataService(configuration);
            var field = form.Fields[0];
            field.LookupDatabaseConnectionName = "ExternalReferenceData";
            await service.ValidateConfigurationAsync(form);
            Assert.Equal("Australia", Assert.Single(await service.SearchAsync(form, field, "Aus")).Text);
            Assert.Equal("Australia", (await service.ResolveAsync(form, field, "12"))!.Text);
            var values = new Dictionary<string, object?> { ["CountryId"] = "12" };
            await service.ValidateValuesAsync(form, values);
            Assert.Equal(12, Assert.IsType<int>(values["CountryId"]));
            await destination.ExecuteAsync("ALTER TABLE LookupDestination ALTER COLUMN CountryId nvarchar(20) NULL;");
            await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => service.ValidateConfigurationAsync(form));
            await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => service.ResolveAsync(form, field, "12"));
            field.LookupDatabaseConnectionName = "NotConfigured";
            await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => service.SearchAsync(form, field, ""));
        }
        finally
        {
            await referenceDatabase.DisposeAsync();
        }
    }

    [LocalDbFact]
    public async Task LookupSearchResolvesKeysBeyondPageAndStoresIntegerNotLabel()
    {
        var (service, form) = await CreateLookupAsync();
        using var connection = await OpenAsync();
        await connection.ExecuteAsync("INSERT INTO LookupCountries VALUES (@Id, 'Country')", Enumerable.Range(1, 60).Select(value => new { Id = value }));
        await service.ValidateConfigurationAsync(form);
        Assert.Equal(50, (await service.SearchAsync(form, form.Fields[0], "Country")).Count);
        Assert.Equal("60", (await service.ResolveAsync(form, form.Fields[0], "60"))!.Value);
        var values = new Dictionary<string, object?> { ["CountryId"] = "60" };
        await service.ValidateValuesAsync(form, values);
        Assert.Equal(60, Assert.IsType<int>(values["CountryId"]));
        values["CountryId"] = "Country";
        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => service.ValidateValuesAsync(form, values));
        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => service.ValidateValuesAsync(form,
            new() { ["countryid"] = "Not a key" }));
        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => service.ValidateValuesAsync(form,
            new() { ["countryid"] = "1", ["CountryId"] = "2" }));
        Assert.Null(await service.ResolveAsync(form, form.Fields[0], "999999999999999999999"));
        await connection.ExecuteAsync("DELETE FROM LookupCountries WHERE Id = 60");
        Assert.Null(await service.ResolveAsync(form, form.Fields[0], "60"));
    }

    [LocalDbFact]
    public async Task LookupStringKeysAndWildcardsAreLiteral()
    {
        var (service, form) = await CreateLookupAsync("nvarchar(20)");
        using var connection = await OpenAsync();
        await connection.ExecuteAsync("INSERT INTO LookupCountries VALUES ('001', '100% valid'), ('002', '1000 other'), ('003', NULL)");
        Assert.Equal("001", Assert.Single(await service.SearchAsync(form, form.Fields[0], "100%")).Value);
        Assert.Equal("001", (await service.ResolveAsync(form, form.Fields[0], "001"))!.Value);
        Assert.Equal("003", (await service.ResolveAsync(form, form.Fields[0], "003"))!.Text);
        Assert.Null(await service.ResolveAsync(form, form.Fields[0], "' OR 1=1--"));
    }

    [LocalDbFact]
    public async Task LookupGuidKeysAreTyped()
    {
        var (service, form) = await CreateLookupAsync("uniqueidentifier");
        var key = Guid.NewGuid();
        using var connection = await OpenAsync();
        await connection.ExecuteAsync("INSERT INTO LookupCountries VALUES (@Key, 'Country')", new { Key = key });
        var values = new Dictionary<string, object?> { ["CountryId"] = key.ToString() };
        await service.ValidateValuesAsync(form, values);
        Assert.Equal(key, Assert.IsType<Guid>(values["CountryId"]));
    }

    [LocalDbFact]
    public async Task LookupConfigurationRejectsNonUniqueKeysAndIncompatibleTypes()
    {
        var (service, form) = await CreateLookupAsync();
        form.Fields[0].LookupKeyColumn = "Name";
        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => service.ValidateConfigurationAsync(form));
        form.Fields[0].LookupKeyColumn = "Id";
        using var connection = await OpenAsync();
        await connection.ExecuteAsync("DROP TABLE LookupDestination; CREATE TABLE LookupDestination (CountryId uniqueidentifier NULL)");
        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => service.ValidateConfigurationAsync(form));
    }

    [LocalDbFact]
    public async Task LookupMetadataRoundTripsAndSurvivesDelegatedSave()
    {
        var (service, form) = await CreateLookupAsync();
        form.Fields[0].LookupDatabaseConnectionName = "DefaultConnection";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:DefaultConnection"] = ConnectionString }).Build();
        var definitions = new FormDefinitionService(configuration, NullLogger<FormDefinitionService>.Instance, service);
        form.Name = "Lookup form";
        form.CreatedBy = "Admin";
        form.Fields[0].GridColumnSpan = 1;
        await definitions.CreateFormDefinitionAsync(form);
        var loaded = (await definitions.GetFormDefinitionAsync(form.Id))!;
        Assert.Equal("LookupCountries", Assert.Single(loaded.Fields).LookupTable);
        Assert.Equal(FieldOptionSource.DatabaseLookup, loaded.Fields[0].OptionSource);
        Assert.Equal("DefaultConnection", loaded.Fields[0].LookupDatabaseConnectionName);
        var update = UpdateFormDesignDto.FromForm(loaded);
        update.Fields[0].DisplayName = "Country label";
        await _repository.SaveAsync(update, new(), true, "Admin");
        loaded = (await definitions.GetFormDefinitionAsync(form.Id))!;
        Assert.Equal("LookupCountries", loaded.Fields[0].LookupTable);
        Assert.Equal("Country label", loaded.Fields[0].DisplayName);
        Assert.Equal("DefaultConnection", loaded.Fields[0].LookupDatabaseConnectionName);
        loaded.Fields[0].LookupDatabaseConnectionName = "ReferenceData";
        await definitions.UpdateFormDefinitionAsync(loaded);
        Assert.Equal("Id", (await definitions.GetFormDefinitionAsync(form.Id))!.Fields[0].LookupKeyColumn);
        Assert.Equal("ReferenceData", (await definitions.GetFormDefinitionAsync(form.Id))!.Fields[0].LookupDatabaseConnectionName);
        Assert.Equal("ReferenceData", Assert.Single(await definitions.GetFormDefinitionsAsync()).Fields[0].LookupDatabaseConnectionName);
        Assert.Equal("ReferenceData", Assert.Single(await definitions.GetActiveAsync()).Fields[0].LookupDatabaseConnectionName);
        Assert.Equal("ReferenceData", Assert.Single(await definitions.GetFormDefinitionsForUserAsync("Admin", new() { "Admin" })).Fields[0].LookupDatabaseConnectionName);
    }

    [LocalDbFact]
    public async Task RequestApplicationStoresKeyAndRejectsDeletedReference()
    {
        var (lookups, form) = await CreateLookupAsync();
        using var connection = await OpenAsync();
        await connection.ExecuteAsync("INSERT INTO LookupCountries VALUES (12, 'Australia')");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DatabaseConnections:ReferenceData"] = ConnectionString }).Build();
        var definitions = new Mock<IFormDefinitionService>();
        definitions.Setup(service => service.GetFormDefinitionAsync(1)).ReturnsAsync(form);
        var repository = new Mock<IFormRequestRepository>();
        var application = new FormRequestApplicationService(repository.Object, Mock.Of<IFormRequestHistoryService>(),
            definitions.Object, new DataService(configuration, NullLogger<DataService>.Instance), Mock.Of<IAdvancedNotificationService>(),
            Mock.Of<IWorkflowProgressService>(), Mock.Of<IDbConnectionFactory>(), configuration,
            NullLogger<FormRequestApplicationService>.Instance, lookups);
        var request = new FormRequest { FormDefinitionId = 1, RequestType = RequestType.Insert, FieldValues = new() { ["CountryId"] = "12" } };
        Assert.True((await application.ApplyChangesToDatabaseAsync(request)).Success);
        Assert.Equal(12, await connection.QuerySingleAsync<int>("SELECT CountryId FROM LookupDestination"));
        await connection.ExecuteAsync("INSERT INTO LookupCountries VALUES (1, 'Boolean-like label')");
        request.FieldValues["CountryId"] = "True";
        Assert.False((await application.ApplyChangesToDatabaseAsync(request)).Success);
        request.FieldValues["CountryId"] = "12";
        await connection.ExecuteAsync("DELETE FROM LookupCountries WHERE Id = 12");
        Assert.False((await application.ApplyChangesToDatabaseAsync(request)).Success);
        Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM LookupDestination"));
    }

    public async Task DisposeAsync()
    {
        if (!_databaseCreated) return;
        using var master = new SqlConnection(_masterConnection);
        await master.OpenAsync();
        await master.ExecuteAsync($"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]");
    }
}