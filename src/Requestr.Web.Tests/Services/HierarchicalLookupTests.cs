using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using Moq;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Web.Components.Shared;
using Requestr.Web.Components;
using Requestr.Web.Services;
using Xunit;

namespace Requestr.Web.Tests.Services;

public class HierarchicalLookupTests : TestContext
{
    private readonly Mock<IFormLookupService> _lookup = new();
    private readonly FormField _field = new()
    {
        Name = "ProgramId", DisplayName = "Program", OptionSource = FieldOptionSource.DatabaseLookup,
        LookupFilterLevels = new() { new() { Column = "Department", Label = "Department" }, new() { Column = "Category", Label = "Category" } }
    };
    private readonly string[] _path = { "\"Community\"", "\"Online\"" };

    public HierarchicalLookupTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_lookup.Object);
        _lookup.Setup(service => service.SearchFilterLevelAsync(1, "ProgramId", It.IsAny<int>(), It.IsAny<IReadOnlyList<string>>(), null, "", 0, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int form, string field, int level, IReadOnlyList<string> filters, string? parent, string? search, int offset, int? request, FormDefinition? draft, CancellationToken token) =>
                new LookupFilterPage(new[] { new LookupOption(_path[level], level == 0 ? "Community" : "Online") }, false));
        _lookup.Setup(service => service.SearchFilteredAsync(1, "ProgramId", "", null, It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new LookupOption("95", "Literacy") });
        _lookup.Setup(service => service.ResolveFilteredAsync(1, "ProgramId", "95", null, It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LookupOption("95", "Literacy"));
        _lookup.Setup(service => service.ResolvePathAsync(1, "ProgramId", "95", null, true, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LookupSelectionPath(new("95", "Literacy"), _path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FiltersUnlockInOrderAndOnlyFinalKeyIsEmitted(bool normalizeEmpty)
    {
        var values = new List<string?>();
        IRenderedComponent<FormFieldSelect>? cut = null;
        cut = RenderComponent<FormFieldSelect>(parameters => parameters.Add(component => component.FormId, 1)
            .Add(component => component.Field, _field).Add(component => component.ValueChanged, value =>
            {
                values.Add(value);
                cut?.SetParametersAndRender(updated => updated.Add(component => component.Value, normalizeEmpty ? value ?? "" : value));
            }));
        Assert.False(cut.Find("input[aria-label='Department']").HasAttribute("disabled"));
        Assert.True(cut.Find("input[aria-label='Category']").HasAttribute("disabled"));
        Assert.True(cut.Find("input[aria-label='Program']").HasAttribute("disabled"));
        cut.Find("input[aria-label='Department']").Focus();
        cut.Find("[role='option']").Click();
        Assert.False(cut.Find("input[aria-label='Category']").HasAttribute("disabled"));
        cut.Find("input[aria-label='Category']").Focus();
        cut.Find("input[aria-label='Department']").Blur();
        cut.Find("[role='option']").Click();
        Assert.False(cut.Find("input[aria-label='Program']").HasAttribute("disabled"));
        cut.Find("input[aria-label='Category']").Blur();
        cut.Find("input[aria-label='Program']").Focus();
        cut.Find("[role='option']").Click();
        Assert.Equal("95", values.Last());
        Assert.All(values.SkipLast(1), value => Assert.Null(value));
        _lookup.Verify(service => service.SearchFilteredAsync(1, "ProgramId", "", null,
            It.Is<IReadOnlyList<string>>(filters => filters.SequenceEqual(_path)), null, null, It.IsAny<CancellationToken>()), Times.Once);
        cut.Find(".lookup-filter-level button[aria-label='Clear selection']").Click();
        Assert.Null(values.Last());
        Assert.Equal("", cut.Find("input[aria-label='Category']").GetAttribute("value"));
        Assert.Equal("", cut.Find("input[aria-label='Program']").GetAttribute("value"));
        Assert.True(cut.Find("input[aria-label='Program']").HasAttribute("disabled"));
    }

    [Fact]
    public void ExistingKeyRestoresFiltersAndExternalChangesResetThePath()
    {
        var cut = RenderComponent<FormFieldSelect>(parameters => parameters.Add(component => component.FormId, 1)
            .Add(component => component.Field, _field).Add(component => component.Value, "95"));
        cut.WaitForAssertion(() => Assert.Equal("Community", cut.Find("input[aria-label='Department']").GetAttribute("value")));
        Assert.Equal("Online", cut.Find("input[aria-label='Category']").GetAttribute("value"));
        Assert.Equal("Literacy", cut.Find("input[aria-label='Program']").GetAttribute("value"));
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.Value, (string?)null));
        Assert.Equal("", cut.Find("input[aria-label='Department']").GetAttribute("value"));
        Assert.True(cut.Find("input[aria-label='Category']").HasAttribute("disabled"));
    }

    [Fact]
    public void ChangingExternalParentClearsKeyOutsideItsScope()
    {
        _field.LookupParentField = "Region";
        _lookup.Setup(service => service.ResolvePathAsync(1, "ProgramId", "95", "North", true, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LookupSelectionPath(new("95", "Literacy"), _path));
        string? emitted = "95";
        var cut = RenderComponent<FormFieldSelect>(parameters => parameters.Add(component => component.FormId, 1)
            .Add(component => component.Field, _field).Add(component => component.Value, "95")
            .Add(component => component.ParentValue, "North").Add(component => component.ValueChanged, value => emitted = value));
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.ParentValue, "South"));
        cut.WaitForAssertion(() => Assert.Null(emitted));
        Assert.Equal("", cut.Find("input[aria-label='Department']").GetAttribute("value"));
    }

    [Fact]
    public void ThreeLevelsRestoreAndChangingMiddleClearsOnlyFollowingSelections()
    {
        _field.LookupFilterLevels.Add(new() { Column = "Type", Label = "Type" });
        _lookup.Setup(service => service.ResolvePathAsync(1, "ProgramId", "95", null, true, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LookupSelectionPath(new("95", "Literacy"), new[] { "\"Community\"", "\"Online\"", "\"Workshop\"" }));
        IRenderedComponent<FormFieldSelect>? cut = null;
        cut = RenderComponent<FormFieldSelect>(parameters => parameters.Add(component => component.FormId, 1)
            .Add(component => component.Field, _field).Add(component => component.Value, "95")
            .Add(component => component.ValueChanged, value => cut?.SetParametersAndRender(updated => updated.Add(component => component.Value, value))));
        Assert.Equal("Workshop", cut.Find("input[aria-label='Type']").GetAttribute("value"));
        cut.Find(".lookup-filter-level:nth-child(2) button[aria-label='Clear selection']").Click();
        Assert.Equal("Community", cut.Find("input[aria-label='Department']").GetAttribute("value"));
        Assert.Equal("", cut.Find("input[aria-label='Type']").GetAttribute("value"));
        Assert.True(cut.Find("input[aria-label='Type']").HasAttribute("disabled"));
        Assert.Equal("", cut.Find("input[aria-label='Program']").GetAttribute("value"));
    }

    [Fact]
    public async Task OldPathResolutionCannotOverwriteNewSelection()
    {
        var pending = new TaskCompletionSource<LookupSelectionPath?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _lookup.Setup(service => service.ResolvePathAsync(1, "ProgramId", "95", null, true, null, null, It.IsAny<CancellationToken>())).Returns(pending.Task);
        _lookup.Setup(service => service.ResolvePathAsync(1, "ProgramId", "96", null, true, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LookupSelectionPath(new("96", "Other"), new[] { "\"Education\"", "\"Onsite\"" }));
        _lookup.Setup(service => service.ResolveFilteredAsync(1, "ProgramId", "96", null, It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LookupOption("96", "Other"));
        var cut = RenderComponent<FormFieldSelect>(parameters => parameters.Add(component => component.FormId, 1)
            .Add(component => component.Field, _field).Add(component => component.Value, "95"));
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.Value, "96"));
        await cut.InvokeAsync(() => pending.SetResult(new(new("95", "Literacy"), _path)));
        cut.WaitForAssertion(() => Assert.Equal("Education", cut.Find("input[aria-label='Department']").GetAttribute("value")));
        Assert.Equal("Other", cut.Find("input[aria-label='Program']").GetAttribute("value"));
    }

    [Fact]
    public void ReadOnlyHierarchyResolvesRecordedKeyWithoutCurrentParentFilter()
    {
        _field.LookupParentField = "Region";
        _lookup.Setup(service => service.ResolvePathAsync(1, "ProgramId", "95", null, false, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LookupSelectionPath(new("95", "Literacy"), _path));
        var cut = RenderComponent<FormFieldSelect>(parameters => parameters.Add(component => component.FormId, 1)
            .Add(component => component.Field, _field).Add(component => component.Value, "95").Add(component => component.Disabled, true));
        Assert.All(cut.FindAll("input"), input => Assert.True(input.HasAttribute("disabled")));
        Assert.Equal("Literacy", cut.Find("input[aria-label='Program']").GetAttribute("value"));
    }

    [Fact]
    public void FormRendererKeepsFilterChoicesAndStoresOnlyFinalField()
    {
        Services.AddLogging();
        Services.AddSingleton(Mock.Of<IWorkflowDesignerService>());
        _field.ControlType = "searchable-select";
        var values = new Dictionary<string, object?>();
        var cut = RenderComponent<FormRenderer>(parameters => parameters
            .Add(component => component.FormDefinition, new FormDefinition { Id = 1, Fields = new() { _field } })
            .Add(component => component.FieldValues, values));
        cut.Find("input[aria-label='Department']").Focus();
        cut.Find("[role='option']").Click();
        Assert.Equal("Community", cut.Find("input[aria-label='Department']").GetAttribute("value"));
        cut.Find("input[aria-label='Department']").Blur();
        cut.Find("input[aria-label='Category']").Focus();
        cut.Find("[role='option']").Click();
        cut.Find("input[aria-label='Category']").Blur();
        cut.Find("input[aria-label='Program']").Focus();
        cut.Find("[role='option']").Click();
        Assert.Equal("95", values["ProgramId"]);
        Assert.Single(values);
    }

    [Fact]
    public void DelegatedDraftPreviewCannotQueryHierarchy()
    {
        _lookup.Invocations.Clear();
        var cut = RenderComponent<CascadingValue<FormDefinition>>(parameters => parameters
            .Add(component => component.Name, "DraftForm")
            .Add(component => component.Value, new FormDefinition { Fields = new() { _field } })
            .AddChildContent<FormFieldSelect>(field => field.Add(component => component.FormId, 1)
                .Add(component => component.Field, _field).Add(component => component.Value, "95")));
        Assert.All(cut.FindAll("input"), input => Assert.True(input.HasAttribute("disabled")));
        _lookup.VerifyNoOtherCalls();
    }
}