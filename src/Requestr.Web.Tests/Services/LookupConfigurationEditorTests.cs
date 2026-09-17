using AngleSharp.Html.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Web.Components.FormBuilder;
using Xunit;

namespace Requestr.Web.Tests.Services;

public class LookupConfigurationEditorTests : TestContext
{
    private readonly Mock<IDatabaseService> _database = new(MockBehavior.Strict);
    private readonly FormField _field = new()
    {
        ControlType = "searchable-select", OptionSource = FieldOptionSource.DatabaseLookup,
        LookupSchema = "dbo", LookupTable = "Countries", LookupKeyColumn = "Code", LookupLabelColumn = "Name"
    };
    private readonly List<ColumnInfo> _columns = new()
    {
        new() { Name = "Id", DataType = "int", IsPrimaryKey = true },
        new() { Name = "Code", DataType = "nvarchar", IsUnique = true },
        new() { Name = "Name", DataType = "nvarchar" }
    };

    public LookupConfigurationEditorTests()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("Admin");
        authorization.SetRoles("Admin");
        Services.AddSingleton(_database.Object);
        _database.Setup(service => service.GetDatabaseNamesAsync())
            .ReturnsAsync(new List<string> { "ReferenceData", "ExternalReferenceData" });
        _database.Setup(service => service.GetSchemasAsync("ReferenceData"))
            .ReturnsAsync(new List<string> { "dbo", "reference" });
        _database.Setup(service => service.GetTablesAsync("ReferenceData", It.IsAny<string?>()))
            .ReturnsAsync(new List<string> { "Countries", "Regions" });
        _database.Setup(service => service.GetTableColumnsAsync("ReferenceData", It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(_columns);
    }

    private IRenderedComponent<LookupConfigurationEditor> RenderEditor() => RenderComponent<LookupConfigurationEditor>(parameters => parameters
        .Add(component => component.Field, _field).Add(component => component.DatabaseConnectionName, "ReferenceData"));

    private static IHtmlSelectElement Select(IRenderedComponent<LookupConfigurationEditor> editor, int position)
        => (IHtmlSelectElement)editor.Find($"select[aria-label='{new[] { "Schema", "Table", "Key column", "Label column" }[position]}']");

    [Fact]
    public async Task ChangingConnectionResetsDependentSelectionsAndKeepsConnectionDuringLoading()
    {
        var editor = RenderEditor();
        var pending = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _database.Setup(service => service.GetSchemasAsync("ExternalReferenceData")).Returns(pending.Task);
        var change = editor.Find("select[aria-label='Database connection']").ChangeAsync(new ChangeEventArgs { Value = "ExternalReferenceData" });
        try
        {
            editor.WaitForAssertion(() => Assert.True(Select(editor, 0).IsDisabled));
            Assert.Equal("ExternalReferenceData", _field.LookupDatabaseConnectionName);
            Assert.Equal("ExternalReferenceData", ((IHtmlSelectElement)editor.Find("select[aria-label='Database connection']")).Value);
            Assert.Null(_field.LookupSchema);
            Assert.Null(_field.LookupTable);
            Assert.Null(_field.LookupKeyColumn);
            Assert.Null(_field.LookupLabelColumn);
            Assert.DoesNotContain(Select(editor, 0).Options, option => option.Value == "dbo");
        }
        finally
        {
            pending.TrySetResult(new() { "reference" });
            await change;
        }
        _database.Setup(service => service.GetTablesAsync("ExternalReferenceData", "reference"))
            .ReturnsAsync(new List<string> { "Countries" });
        _database.Setup(service => service.GetTableColumnsAsync("ExternalReferenceData", "Countries", "reference"))
            .ReturnsAsync(_columns);
        await Select(editor, 0).ChangeAsync(new ChangeEventArgs { Value = "reference" });
        await Select(editor, 1).ChangeAsync(new ChangeEventArgs { Value = "Countries" });
        Assert.Equal("Id", _field.LookupKeyColumn);
        _database.Verify(service => service.GetTableColumnsAsync("ExternalReferenceData", "Countries", "reference"), Times.Once);
        await editor.Find("select[aria-label='Database connection']").ChangeAsync(new ChangeEventArgs { Value = "" });
        Assert.True(string.IsNullOrEmpty(_field.LookupDatabaseConnectionName));
        Assert.Contains(Select(editor, 0).Options, option => option.Value == "dbo");
        Assert.Null(_field.LookupTable);
    }

    [Fact]
    public void ExistingExternalConnectionLoadsItsMetadataAndPreservesKey()
    {
        _field.LookupDatabaseConnectionName = "ExternalReferenceData";
        _database.Setup(service => service.GetSchemasAsync("ExternalReferenceData")).ReturnsAsync(new List<string> { "dbo" });
        _database.Setup(service => service.GetTablesAsync("ExternalReferenceData", "dbo")).ReturnsAsync(new List<string> { "Countries" });
        _database.Setup(service => service.GetTableColumnsAsync("ExternalReferenceData", "Countries", "dbo")).ReturnsAsync(_columns);
        var editor = RenderEditor();
        Assert.Equal("ExternalReferenceData", ((IHtmlSelectElement)editor.Find("select[aria-label='Database connection']")).Value);
        Assert.Equal("Code", _field.LookupKeyColumn);
        _database.Verify(service => service.GetSchemasAsync("ReferenceData"), Times.Never);
        _database.Verify(service => service.GetTableColumnsAsync("ExternalReferenceData", "Countries", "dbo"), Times.Once);
    }

    [Fact]
    public void UnavailableSavedConnectionStillListsConnectionsForRecovery()
    {
        _field.LookupDatabaseConnectionName = "ExternalReferenceData";
        _database.Setup(service => service.GetSchemasAsync("ExternalReferenceData")).ThrowsAsync(new InvalidOperationException("Unavailable"));
        var editor = RenderEditor();
        var selector = (IHtmlSelectElement)editor.Find("select[aria-label='Database connection']");
        Assert.Equal("ExternalReferenceData", selector.Value);
        Assert.Contains(selector.Options, option => option.Value == "ReferenceData");
        Assert.False(selector.IsDisabled);
        Assert.Contains("Unable to load", editor.Find("[role='alert']").TextContent);
    }

    [Fact]
    public async Task UnavailableConnectionShowsErrorWithoutRevertingSelection()
    {
        var editor = RenderEditor();
        _database.Setup(service => service.GetSchemasAsync("ExternalReferenceData")).ThrowsAsync(new InvalidOperationException("Unavailable"));
        await editor.Find("select[aria-label='Database connection']").ChangeAsync(new ChangeEventArgs { Value = "ExternalReferenceData" });
        Assert.Contains("Unable to load", editor.Find("[role='alert']").TextContent);
        Assert.Equal("ExternalReferenceData", _field.LookupDatabaseConnectionName);
        Assert.False(Select(editor, 0).IsDisabled);
        Assert.Single(Select(editor, 0).Options);
    }

    [Theory]
    [InlineData(0, "reference")]
    [InlineData(1, "Regions")]
    public async Task ParentSelectionRemainsPresentWhileMetadataLoads(int position, string selectedValue)
    {
        var editor = RenderEditor();
        var pending = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _database.Setup(service => service.GetSchemasAsync("ReferenceData")).Returns(pending.Task);
        var change = Select(editor, position).ChangeAsync(new ChangeEventArgs { Value = selectedValue });
        try
        {
            editor.WaitForAssertion(() => Assert.True(Select(editor, position).IsDisabled));
            Assert.Contains(Select(editor, position).Options, option => option.Value == selectedValue);
            Assert.Equal(selectedValue, Select(editor, position).Value);
        }
        finally
        {
            pending.TrySetResult(new() { "dbo", "reference" });
            await change;
        }
        Assert.Equal(selectedValue, Select(editor, position).Value);
    }

    [Fact]
    public async Task ChangingTableSelectsItsSinglePrimaryKeyAndClearsLabel()
    {
        var editor = RenderEditor();
        await Select(editor, 1).ChangeAsync(new ChangeEventArgs { Value = "Regions" });
        Assert.Equal("Id", _field.LookupKeyColumn);
        Assert.Equal("Id", Select(editor, 2).Value);
        Assert.Null(_field.LookupLabelColumn);
        Assert.Equal("Regions", _field.LookupTable);
    }

    [Fact]
    public void LoadingExistingConfigurationKeepsExplicitUniqueKey()
    {
        RenderEditor();
        Assert.Equal("Code", _field.LookupKeyColumn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TablesWithoutSinglePrimaryKeyDoNotChooseKey(bool composite)
    {
        _columns[0].IsPrimaryKey = composite;
        _columns[1].IsPrimaryKey = composite;
        var editor = RenderEditor();
        await Select(editor, 1).ChangeAsync(new ChangeEventArgs { Value = "Regions" });
        Assert.Null(_field.LookupKeyColumn);
    }
}