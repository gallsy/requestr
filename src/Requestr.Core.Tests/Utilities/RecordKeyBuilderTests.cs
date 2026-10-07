using System.Text.Json;
using Requestr.Core.Models;
using Requestr.Core.Services;
using Requestr.Core.Utilities;
using Xunit;

namespace Requestr.Core.Tests.Utilities;

public class RecordKeyBuilderTests
{
    private static Dictionary<string, object?> FromJson(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;

    [Fact]
    public void TypedRowValuesAndStoredJsonProduceTheSameKey()
    {
        var guid = Guid.NewGuid();
        var columns = new List<string> { "Region", "Id", "Ref", "When", "Amount" };
        var row = new Dictionary<string, object?>
        {
            ["Region"] = "TAS ", ["Id"] = 123L, ["Ref"] = guid, ["When"] = new DateTime(2024, 1, 2, 3, 4, 5), ["Amount"] = 1.50m
        };
        var stored = FromJson(JsonSerializer.Serialize(row));

        var fromRow = RecordKeyBuilder.Build(columns, row);
        Assert.Equal(fromRow, RecordKeyBuilder.Build(columns, stored));
        Assert.Equal($$"""{"Region":"TAS","Id":"123","Ref":"{{guid:D}}","When":"2024-01-02T03:04:05","Amount":"1.5"}""", fromRow);
    }

    [Fact]
    public void KeyUsesPrimaryKeyOrderAndMatchesColumnsCaseInsensitively()
    {
        var values = new Dictionary<string, object?> { ["area"] = 7, ["CODE"] = "AU", ["Name"] = "Australia" };
        var key = RecordKeyBuilder.Build(["Code", "Area"], values);
        Assert.Equal("""{"Code":"AU","Area":"7"}""", key);
        Assert.Equal("Code = AU, Area = 7", RecordKeyBuilder.Describe(key));
    }

    [Fact]
    public void MissingOrEmptyKeyValuesProduceNoKey()
    {
        Assert.Null(RecordKeyBuilder.Build([], new Dictionary<string, object?> { ["Id"] = 1 }));
        Assert.Null(RecordKeyBuilder.Build(["Id"], new Dictionary<string, object?> { ["Name"] = "x" }));
        Assert.Null(RecordKeyBuilder.Build(["Id"], new Dictionary<string, object?> { ["Id"] = null }));
        Assert.Null(RecordKeyBuilder.Build(["Id"], FromJson("""{"Id":null}""")));
        Assert.Null(RecordKeyBuilder.Build(["Id"], new Dictionary<string, object?> { ["Id"] = new string('x', RecordKeyBuilder.MaxLength) }));
    }

    [Fact]
    public void InsertsUseFieldValuesAndOtherRequestsUseOriginalValues()
    {
        var fields = new Dictionary<string, object?> { ["Id"] = 2 };
        var original = new Dictionary<string, object?> { ["Id"] = 1 };
        Assert.Equal("""{"Id":"2"}""", RecordKeyBuilder.ForRequest(["Id"], RequestType.Insert, fields, original));
        Assert.Equal("""{"Id":"1"}""", RecordKeyBuilder.ForRequest(["Id"], RequestType.Update, fields, original));
        Assert.Equal("""{"Id":"1"}""", RecordKeyBuilder.ForRequest(["Id"], RequestType.Delete, fields, original));
    }

    [Theory]
    [InlineData("{DapperRow, Id = '42'}", "Id", """{"Id":"42"}""")]
    [InlineData("{DapperRow, ID = '92588'}", "Id", """{"Id":"92588"}""")]
    [InlineData("{DapperRow, id = '1620'}", "ID", """{"ID":"1620"}""")]
    [InlineData("42", "Id", """{"Id":"42"}""")]
    [InlineData("{DapperRow, Other = '42'}", "Id", null)]
    [InlineData("Id=42", "Id", null)]
    [InlineData(null, "Id", null)]
    public void LegacyAppliedKeysAreRecoveredForSingleColumnKeys(string? legacy, string column, string? expected)
    {
        Assert.Equal(expected, RecordKeyBuilder.FromLegacyAppliedKey(legacy, [column]));
        Assert.Null(RecordKeyBuilder.FromLegacyAppliedKey(legacy, [column, "Second"]));
    }

    [Fact]
    public void InsertedIdentityIsWrittenBackFromDapperRows()
    {
        var values = new Dictionary<string, object?> { ["Name"] = "x" };
        RecordKeyBuilder.ApplyInsertedIdentity(values, new Dictionary<string, object> { ["Id"] = 5m }, "Id");
        Assert.Equal(5L, values["Id"]);
        RecordKeyBuilder.ApplyInsertedIdentity(values, 9, null);
        Assert.Equal(5L, values["Id"]);
    }

    [Fact]
    public void HistoryChangesShowInsertedValuesAndOnlyChangedUpdateFields()
    {
        var original = FromJson("""{"Id":1,"Name":"Tasmania","Active":true,"Since":"2024-01-01T00:00:00","Rate":1.50}""");
        var update = FromJson("""{"Name":"Tas","Active":"True","Since":"2024-01-01","Rate":"1.5"}""");

        var change = Assert.Single(RecordHistoryService.BuildChanges(RequestType.Update, update, original));
        Assert.Equal("Name", change.FieldName);
        Assert.Equal("Tasmania", ((JsonElement)change.OldValue!).GetString());

        var inserted = RecordHistoryService.BuildChanges(RequestType.Insert, FromJson("""{"Name":"Tas","Notes":"","Code":null}"""), new Dictionary<string, object?>());
        Assert.Equal("Name", Assert.Single(inserted).FieldName);
        Assert.Empty(RecordHistoryService.BuildChanges(RequestType.Delete, update, original));
    }
}
