using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Moq;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Web.Components.Shared;
using Requestr.Web.Components.FormBuilder;
using Requestr.Web.Services;
using Xunit;

namespace Requestr.Web.Tests.Services;

public class LookupComponentTests : TestContext
{
    public LookupComponentTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public async Task FilterChoicesCanLoadMoreAndSearchRestartsPaging()
    {
        var calls = new List<(string Query, int Offset)>();
        var cut = RenderComponent<SearchableSelect>(parameters => parameters
            .Add(component => component.AccessibleLabel, "Category")
            .Add(component => component.ShowOptionValues, false)
            .Add(component => component.PagedSearchOptions, (query, offset, token) =>
            {
                calls.Add((query, offset));
                return Task.FromResult(new LookupFilterPage(new[] { new LookupOption($"token-{offset}", $"Category {offset}") }, offset == 0));
            }));
        cut.Find("input[aria-label='Category']").Focus();
        cut.Find("button[aria-label='Load more options']").Click();
        Assert.Equal(2, cut.FindAll("[role='option']").Count);
        Assert.Empty(cut.FindAll("button[aria-label='Load more options']"));
        Assert.DoesNotContain("token-", cut.Markup);
        await cut.Find("input").InputAsync(new() { Value = "New" });
        Assert.Equal(new[] { ("", 0), ("", 1), ("New", 0) }, calls);
        Assert.Single(cut.FindAll("[role='option']"));
    }

    [Fact]
    public async Task LookupTypingNeverEmitsLabelAndDuplicateLabelsSelectCorrectKey()
    {
        string? emitted = "initial";
        var cut = RenderComponent<SearchableSelect>(parameters => parameters
            .Add(component => component.SearchOptions, (search, token) => Task.FromResult<IReadOnlyList<(string, string)>>(new[] { ("1", "Country"), ("2", "Country") }))
            .Add(component => component.ValueChanged, value => emitted = value));
        await cut.Find("input").InputAsync(new() { Value = "Country" });
        Assert.Null(emitted);
        Assert.Equal(2, cut.FindAll("[role='option']").Count);
        cut.Find("[role='option']:last-child").Click();
        Assert.Equal("2", emitted);
        Assert.Equal("Country", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void ExistingKeyResolvesOutsideSearchPageAndMissingKeyIsPreserved()
    {
        var cut = RenderComponent<SearchableSelect>(parameters => parameters
            .Add(component => component.Value, "99")
            .Add(component => component.SearchOptions, (search, token) => Task.FromResult<IReadOnlyList<(string, string)>>(Array.Empty<(string, string)>()))
            .Add(component => component.ResolveOption, (value, token) => Task.FromResult<(string, string)?>(value == "99" ? ("99", "Country 99") : null)));
        cut.WaitForAssertion(() => Assert.Equal("Country 99", cut.Find("input").GetAttribute("value")));
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.Value, "100"));
        cut.WaitForAssertion(() => Assert.Contains("Unavailable lookup key: 100", cut.Markup));
        Assert.Equal("100", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public async Task SlowSearchCannotOverwriteNewerResults()
    {
        var oldResult = new TaskCompletionSource<IReadOnlyList<(string, string)>>();
        var started = new TaskCompletionSource();
        var cut = RenderComponent<SearchableSelect>(parameters => parameters.Add(component => component.SearchOptions, (query, token) =>
        {
            if (query == "Old") { started.SetResult(); return oldResult.Task; }
            return Task.FromResult<IReadOnlyList<(string, string)>>(new[] { ("2", "New result") });
        }));
        var oldInput = cut.Find("input").InputAsync(new() { Value = "Old" });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cut.Find("input").InputAsync(new() { Value = "New" });
        oldResult.SetResult(new[] { ("1", "Old result") });
        await oldInput;
        Assert.Contains("New result", cut.Markup);
        Assert.DoesNotContain("Old result", cut.Markup);
    }

    [Fact]
    public async Task SearchFailureHasExplicitErrorAndNoSelectableOldResults()
    {
        var cut = RenderComponent<SearchableSelect>(parameters => parameters.Add(component => component.SearchOptions,
            (query, token) => Task.FromException<IReadOnlyList<(string, string)>>(new InvalidOperationException("database unavailable"))));
        await cut.Find("input").FocusAsync(new());
        Assert.Contains("Unable to load lookup options", cut.Markup);
        Assert.Empty(cut.FindAll("[role='option']"));
    }

    [Fact]
    public void NumericJsonValuesDisplayLookupLabelsAndUnderlyingKeys()
    {
        var lookup = new Mock<IFormLookupService>();
        lookup.Setup(service => service.ResolveAsync(1, "CountryId", "12", 10, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(new LookupOption("12", "Australia"));
        Services.AddSingleton(lookup.Object);
        var cut = RenderComponent<FieldValueDisplay>(parameters => parameters
            .Add(component => component.IsLookup, true).Add(component => component.FormId, 1)
            .Add(component => component.RequestId, 10).Add(component => component.FieldName, "CountryId")
            .Add(component => component.NewValue, JsonSerializer.Deserialize<JsonElement>("12")));
        cut.WaitForAssertion(() => Assert.Contains("Australia (12)", cut.Markup));
    }

    [Fact]
    public async Task UnselectedSearchBlocksOptionalFieldSubmissionUntilCleared()
    {
        var service = new Mock<IFormLookupService>();
        service.Setup(lookup => lookup.SearchAsync(1, "CountryId", It.IsAny<string?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new LookupOption("1", "Country") });
        Services.AddSingleton(service.Object);
        var context = new EditContext(new Dictionary<string, string?>());
        var cut = RenderComponent<CascadingValue<EditContext>>(parameters => parameters.Add(component => component.Value, context)
            .AddChildContent<FormFieldSelect>(field => field.Add(component => component.FormId, 1)
                .Add(component => component.Field, new FormField { Name = "CountryId", DisplayName = "Country", OptionSource = FieldOptionSource.DatabaseLookup })));
        await cut.Find("input").InputAsync(new() { Value = "Not selected" });
        Assert.False(context.Validate());
        cut.Find("button[aria-label='Clear selection']").Click();
        cut.WaitForAssertion(() => Assert.True(context.Validate()));
    }

    [Fact]
    public async Task AdminLookupEditorUsesConfiguredConnection()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("Admin");
        authorization.SetRoles("Admin");
        Services.AddLogging();
        Services.AddBlazorBootstrap();
        var database = new Mock<IDatabaseService>(MockBehavior.Strict);
        database.Setup(service => service.GetDatabaseNamesAsync()).ReturnsAsync(new List<string> { "ReferenceData" });
        database.Setup(service => service.GetSchemasAsync("ReferenceData")).ReturnsAsync(new List<string> { "dbo" });
        database.Setup(service => service.GetTablesAsync("ReferenceData", "dbo")).ReturnsAsync(new List<string> { "Countries" });
        database.Setup(service => service.GetTableColumnsAsync("ReferenceData", "Countries", "dbo"))
            .ReturnsAsync(new List<ColumnInfo> { new() { Name = "Id", DataType = "int", IsPrimaryKey = true }, new() { Name = "Name", DataType = "nvarchar" } });
        Services.AddSingleton(database.Object);
        var cut = RenderComponent<FieldConfigurationModal>(parameters => parameters
            .Add(component => component.DatabaseConnectionName, "ReferenceData")
            .Add(component => component.SqlType, "int")
            .Add(component => component.Field, new FormField { ControlType = "searchable-select", OptionSource = FieldOptionSource.DatabaseLookup,
                LookupSchema = "dbo", LookupTable = "Countries", LookupKeyColumn = "Id", LookupLabelColumn = "Name" }));
        await cut.InvokeAsync(cut.Instance.ShowAsync);
        cut.WaitForAssertion(() => Assert.Contains("Name", cut.FindComponent<LookupConfigurationEditor>().Markup));
        Assert.Equal("ReferenceData", cut.FindComponent<LookupConfigurationEditor>().Instance.DatabaseConnectionName);
        database.Verify(service => service.GetTableColumnsAsync("ReferenceData", "Countries", "dbo"), Times.Once);
    }

    [Fact]
    public async Task DelegatedLookupEditorDoesNotRenderSourcesOrStaticOptions()
    {
        Services.AddLogging();
        Services.AddBlazorBootstrap();
        var cut = RenderComponent<FieldConfigurationModal>(parameters => parameters
            .Add(component => component.PresentationOnly, true)
            .Add(component => component.Field, new FormField { ControlType = "searchable-select", OptionSource = FieldOptionSource.DatabaseLookup }));
        await cut.InvokeAsync(cut.Instance.ShowAsync);
        Assert.Empty(cut.FindComponents<LookupConfigurationEditor>());
        Assert.DoesNotContain("Options (one per line)", cut.Markup);
        Assert.Contains("Help Text", cut.Markup);
    }

    [Fact]
    public void StaticSelectStillUsesConfiguredValueTextPairs()
    {
        string? selected = null;
        var cut = RenderComponent<SearchableSelect>(parameters => parameters
            .Add(component => component.Options, new[] { ("1", "Country") })
            .Add(component => component.ValueChanged, value => selected = value));
        cut.Find("input").Focus();
        cut.Find("[role='option']").Click();
        Assert.Equal("1", selected);
    }
}