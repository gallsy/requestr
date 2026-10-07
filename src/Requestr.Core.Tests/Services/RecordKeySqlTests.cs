using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Repositories;
using Requestr.Core.Services;
using Requestr.Core.Services.FormRequests;
using Requestr.Core.Services.Workflow;
using Xunit;

namespace Requestr.Core.Tests.Services;

public class RecordKeySqlTests : IAsyncLifetime
{
    private readonly string _databaseName = $"RequestrRecordKeyTest_{Guid.NewGuid():N}";
    private readonly string _masterConnection = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;Encrypt=false;Pooling=false";
    private string ConnectionString => new SqlConnectionStringBuilder(_masterConnection) { InitialCatalog = _databaseName }.ConnectionString;
    private bool _databaseCreated;
    private IDbConnectionFactory _factory = default!;
    private DataService _dataService = default!;

    public async Task InitializeAsync()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("REQUESTR_RUN_SQL_TESTS") != "1") return;
        using (var master = new SqlConnection(_masterConnection))
        {
            await master.OpenAsync();
            await master.ExecuteAsync($"CREATE DATABASE [{_databaseName}]");
        }
        _databaseCreated = true;
        using var connection = await OpenAsync();
        await connection.ExecuteAsync("""
            CREATE TABLE FormDefinitions (Id int IDENTITY PRIMARY KEY, Name nvarchar(255) NOT NULL,
                DatabaseConnectionName nvarchar(255) NOT NULL DEFAULT 'ReferenceData', TableName nvarchar(255) NOT NULL,
                [Schema] nvarchar(255) NOT NULL DEFAULT 'dbo');
            CREATE TABLE Users (UserObjectId uniqueidentifier PRIMARY KEY, DisplayName nvarchar(255) NOT NULL);
            CREATE TABLE FormRequests (Id int IDENTITY PRIMARY KEY, FormDefinitionId int NOT NULL, RequestType int NOT NULL,
                FieldValues nvarchar(max) NOT NULL, OriginalValues nvarchar(max) NULL, Status int NOT NULL,
                RequestedBy nvarchar(255) NOT NULL, RequestedAt datetime2 NOT NULL, ApprovedAt datetime2 NULL,
                AppliedRecordKey nvarchar(255) NULL, BulkFormRequestId int NULL);
            CREATE TABLE BulkFormRequests (Id int IDENTITY PRIMARY KEY, FormDefinitionId int NOT NULL, RequestType int NOT NULL,
                Status int NOT NULL, RequestedBy nvarchar(255) NOT NULL, RequestedAt datetime2 NOT NULL, ApprovedAt datetime2 NULL);
            CREATE TABLE BulkFormRequestItems (Id int IDENTITY PRIMARY KEY, BulkFormRequestId int NOT NULL,
                FieldValues nvarchar(max) NOT NULL, OriginalValues nvarchar(max) NULL, RowNumber int NOT NULL, Status int NOT NULL);
            CREATE TABLE FormPermissions (Id int IDENTITY PRIMARY KEY, FormDefinitionId int NOT NULL, RoleName nvarchar(255) NOT NULL,
                PermissionType int NOT NULL, IsGranted bit NOT NULL, CreatedBy nvarchar(255) NOT NULL,
                UNIQUE (FormDefinitionId, RoleName, PermissionType));
            CREATE TABLE Countries (Id int IDENTITY PRIMARY KEY, Name nvarchar(100) NOT NULL);
            CREATE TABLE Regions (Code char(3) NOT NULL, Area int NOT NULL, Name nvarchar(100) NULL, PRIMARY KEY (Code, Area));
            INSERT INTO FormDefinitions (Name, TableName) VALUES ('Countries', 'Countries'), ('Country admin', 'Countries'), ('Regions', 'Regions');
            INSERT INTO FormPermissions (FormDefinitionId, RoleName, PermissionType, IsGranted, CreatedBy)
            VALUES (1, 'Admin', 10, 1, 'test'), (2, 'Admin', 10, 0, 'test'), (3, 'Readers', 10, 1, 'test');
            """);
        await ApplyMigrationAsync(connection);
        await ApplyMigrationAsync(connection);

        var factory = new Mock<IDbConnectionFactory>();
        factory.Setup(f => f.CreateConnectionAsync()).Returns(OpenAsync);
        factory.SetupGet(f => f.DefaultCommandTimeout).Returns(30);
        _factory = factory.Object;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DatabaseConnections:ReferenceData"] = ConnectionString }).Build();
        _dataService = new DataService(configuration, NullLogger<DataService>.Instance);
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ApplyMigrationAsync(SqlConnection connection)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "033_RecordKeys.sql"));
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch)) await connection.ExecuteAsync(batch);
        }
    }

    private static Task<int> AddRequestAsync(SqlConnection connection, int formId, RequestType type, RequestStatus status,
        string fieldValues, string? originalValues, int minutesAgo, string? legacyKey = null, int? bulkId = null) =>
        connection.QuerySingleAsync<int>("""
            INSERT INTO FormRequests (FormDefinitionId, RequestType, FieldValues, OriginalValues, Status, RequestedBy, RequestedAt, AppliedRecordKey, BulkFormRequestId)
            OUTPUT INSERTED.Id
            VALUES (@FormId, @Type, @FieldValues, @OriginalValues, @Status, 'user', DATEADD(minute, -@MinutesAgo, SYSUTCDATETIME()), @LegacyKey, @BulkId)
            """, new { FormId = formId, Type = (int)type, FieldValues = fieldValues, OriginalValues = originalValues, Status = (int)status, MinutesAgo = minutesAgo, LegacyKey = legacyKey, BulkId = bulkId });

    private static Task<int> AddBulkAsync(SqlConnection connection, int formId, RequestType type, RequestStatus status,
        RequestStatus itemStatus, string fieldValues, string originalValues, int minutesAgo) =>
        connection.QuerySingleAsync<int>("""
            DECLARE @Bulk TABLE (Id int);
            INSERT INTO BulkFormRequests (FormDefinitionId, RequestType, Status, RequestedBy, RequestedAt)
            OUTPUT INSERTED.Id INTO @Bulk
            VALUES (@FormId, @Type, @Status, 'user', DATEADD(minute, -@MinutesAgo, SYSUTCDATETIME()));
            INSERT INTO BulkFormRequestItems (BulkFormRequestId, FieldValues, OriginalValues, RowNumber, Status)
            SELECT Id, @FieldValues, @OriginalValues, 1, @ItemStatus FROM @Bulk;
            SELECT Id FROM @Bulk;
            """, new { FormId = formId, Type = (int)type, Status = (int)status, ItemStatus = (int)itemStatus, FieldValues = fieldValues, OriginalValues = originalValues, MinutesAgo = minutesAgo });

    [LocalDbFact]
    public async Task MigrationGrantsHistoryToAdminsWhoCanViewData()
    {
        using var connection = await OpenAsync();
        var grants = (await connection.QueryAsync<(int FormId, string Role)>(
            "SELECT FormDefinitionId, RoleName FROM FormPermissions WHERE PermissionType = 12 AND IsGranted = 1")).ToList();
        Assert.Equal((1, "Admin"), Assert.Single(grants));
    }

    [LocalDbFact]
    public async Task BackfillKeysLegacyRequestsAndHistoryFindsThemAcrossFormsOnTheTable()
    {
        using var connection = await OpenAsync();
        var created = await AddRequestAsync(connection, 1, RequestType.Insert, RequestStatus.Applied, """{"Name":"Australia","Id":12}""", "{}", 30);
        var legacyInsert = await AddRequestAsync(connection, 1, RequestType.Insert, RequestStatus.Applied, """{"Name":"Canada"}""", "{}", 25, "{DapperRow, Id = '13'}");
        var otherFormUpdate = await AddRequestAsync(connection, 2, RequestType.Update, RequestStatus.Pending, """{"Name":"Aus"}""", """{"Id":12,"Name":"Australia"}""", 5);
        var unappliedInsert = await AddRequestAsync(connection, 1, RequestType.Insert, RequestStatus.Pending, """{"Name":"Pending"}""", "{}", 1);
        var regionDelete = await AddRequestAsync(connection, 3, RequestType.Delete, RequestStatus.Applied, "{}", """{"Code":"TAS","Area":7}""", 3);
        var bulkId = await AddBulkAsync(connection, 1, RequestType.Update, RequestStatus.Rejected, RequestStatus.Pending,
            """{"Name":"Australia!"}""", """{"Id":12,"Name":"Australia"}""", 10);
        await AddRequestAsync(connection, 1, RequestType.Update, RequestStatus.Rejected, """{"IsBulkRequest":true}""", "{}", 10, bulkId: bulkId);

        var backfill = new RecordKeyBackfillService(_factory, _dataService, NullLogger<RecordKeyBackfillService>.Instance);
        Assert.Equal(new RecordKeyBackfillResult(4, 1, 1, 0), await backfill.RunAsync());
        Assert.Equal(new RecordKeyBackfillResult(0, 1, 0, 0),
            await new RecordKeyBackfillService(_factory, _dataService, NullLogger<RecordKeyBackfillService>.Instance).RunAsync());

        var keys = (await connection.QueryAsync<(int Id, string? Key)>("SELECT Id, RecordKey FROM FormRequests")).ToDictionary(row => row.Id, row => row.Key);
        Assert.Equal("""{"Id":"12"}""", keys[created]);
        Assert.Equal("""{"Id":"13"}""", keys[legacyInsert]);
        Assert.Equal("""{"Id":"12"}""", keys[otherFormUpdate]);
        Assert.Null(keys[unappliedInsert]);
        Assert.Equal("""{"Code":"TAS","Area":"7"}""", keys[regionDelete]);

        var form = new FormDefinition { Id = 1, DatabaseConnectionName = "ReferenceData", Schema = "dbo", TableName = "Countries" };
        var history = await new RecordHistoryService(_factory).GetHistoryAsync(form, """{"Id":"12"}""");
        Assert.Equal(3, history.Count);
        Assert.Equal(otherFormUpdate, history[0].FormRequestId);
        Assert.Equal("Country admin", history[0].FormName);
        Assert.Equal("Name", Assert.Single(history[0].Changes).FieldName);
        Assert.Equal(bulkId, history[1].BulkFormRequestId);
        Assert.Equal(RequestStatus.Rejected, history[1].Status);
        Assert.Equal(created, history[2].FormRequestId);
        Assert.Empty(await new RecordHistoryService(_factory).GetHistoryAsync(form, """{"Id":"99"}"""));
    }

    [LocalDbFact]
    public async Task AppliedInsertReturnsKeyForGeneratedIdentity()
    {
        var form = new FormDefinition
        {
            Id = 1, DatabaseConnectionName = "ReferenceData", Schema = "dbo", TableName = "Countries",
            Fields = new() { new() { Name = "Name", DataType = "text", SqlDataType = "nvarchar" } }
        };
        var definitions = new Mock<IFormDefinitionService>();
        definitions.Setup(service => service.GetFormDefinitionAsync(1)).ReturnsAsync(form);
        var application = new FormRequestApplicationService(Mock.Of<IFormRequestRepository>(), Mock.Of<IFormRequestHistoryService>(),
            definitions.Object, _dataService, Mock.Of<IAdvancedNotificationService>(), Mock.Of<IWorkflowProgressService>(),
            _factory, new ConfigurationBuilder().Build(), NullLogger<FormRequestApplicationService>.Instance, Mock.Of<ILookupDataService>());

        var result = await application.ApplyChangesToDatabaseAsync(new FormRequest
        {
            FormDefinitionId = 1, RequestType = RequestType.Insert, FieldValues = new() { ["Name"] = "Norway" }
        });

        using var connection = await OpenAsync();
        var id = await connection.QuerySingleAsync<int>("SELECT Id FROM Countries WHERE Name = 'Norway'");
        Assert.True(result.Success);
        Assert.Equal($$"""{"Id":"{{id}}"}""", result.RecordKey);
    }

    public async Task DisposeAsync()
    {
        if (!_databaseCreated) return;
        using var master = new SqlConnection(_masterConnection);
        await master.OpenAsync();
        await master.ExecuteAsync($"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]");
    }
}
