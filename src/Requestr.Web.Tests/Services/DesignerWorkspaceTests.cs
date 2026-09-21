using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Requestr.Core.Models;
using Requestr.Core.Validation;
using Requestr.Web.Components.FormBuilder;
using Xunit;

namespace Requestr.Web.Tests.Services;

public class DesignerWorkspaceTests : TestContext
{
    public DesignerWorkspaceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddBlazorBootstrap();
    }

    [Fact]
    public void SelectingFieldShowsInlinePropertiesAndKeepsSelectionAfterReload()
    {
        var form = new FormDefinition { Name = "Countries", Sections = new() { new() { Id = 1, Name = "Details", MaxColumns = 2 } },
            Fields = new() { new() { Name = "Name", DisplayName = "Country name", FormSectionId = 1, GridColumnSpan = 2 } } };
        var cut = RenderComponent<DesignerWorkspace>(parameters => parameters.Add(component => component.Form, form));
        cut.Find(".canvas-field").Click();
        Assert.True(cut.FindComponent<FieldConfigurationModal>().Instance.Inline);
        Assert.Equal(new[] { "Layout", "Control", "Defaults and Validation", "Behavior", "Help", "Generated Value" },
            cut.FindAll(".designer-properties h3").Select(heading => heading.TextContent.Trim()));
        Assert.Empty(cut.FindAll(".modal"));
        cut.Find(".field-properties input").Change("New label");
        Assert.Equal("New label", form.Fields[0].DisplayName);
        form.Fields[0] = new() { Name = "Name", DisplayName = "Saved label", FormSectionId = 1, GridColumnSpan = 2 };
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.Form, form));
        Assert.Equal("Saved label", cut.FindComponent<FieldConfigurationModal>().Instance.Field!.DisplayName);
    }

    [Fact]
    public void QuickAddTargetsSelectedSection()
    {
        var form = new FormDefinition { Sections = new() { new() { Id = 1, Name = "First" }, new() { Id = 2, Name = "Second", MaxColumns = 2 } } };
        var cut = RenderComponent<DesignerWorkspace>(parameters => parameters.Add(component => component.Form, form)
            .Add(component => component.Columns, new() { new() { Name = "Country" } })
            .Add(component => component.OnAddField, column => form.Fields.Add(new() { Name = column.Name, DisplayName = column.Name, FormSectionId = 1 })));
        cut.Find(".canvas-section:last-child .section-heading").Click();
        cut.Find("button[title='Add to selected section']").Click();
        Assert.Equal(2, Assert.Single(form.Fields).FormSectionId);
        Assert.Equal(2, form.Fields[0].GridColumnSpan);
    }

    [Fact]
    public void DelegatesCannotConfigureConditions()
    {
        var form = new FormDefinition { Fields = new() { new() { Name = "Parent", DisplayName = "Parent" }, new() { Name = "Child", DisplayName = "Child", VisibilityCondition = FormConditions.Serialize(new() { Field = "Parent", Value = "Yes" }) } } };
        var cut = RenderComponent<DesignerWorkspace>(parameters => parameters.Add(component => component.Form, form).Add(component => component.PresentationOnly, true));
        cut.Find(".outline-item:last-child").Click();
        Assert.Empty(cut.FindComponents<ConditionEditor>());
        Assert.Equal(new[] { "Layout", "Control", "Help" }, cut.FindAll(".designer-properties h3").Select(heading => heading.TextContent.Trim()));
        Assert.Empty(cut.FindAll("button[aria-label='Remove field']"));
    }

    [Fact]
    public async Task NumericAndPointerPlacementPreserveGapsAndRejectOverlap()
    {
        var field = new FormField { Name = "First", DisplayName = "First", FormSectionId = 1, GridRow = 3, GridColumnSpan = 1 };
        var other = new FormField { Name = "Second", DisplayName = "Second", FormSectionId = 1, GridRow = 3, GridColumn = 3, GridColumnSpan = 2 };
        var form = new FormDefinition { Sections = new() { new() { Id = 1, MaxColumns = 4 } }, Fields = new() { field, other } };
        var changes = 0;
        var cut = RenderComponent<DesignerWorkspace>(parameters => parameters.Add(component => component.Form, form).Add(component => component.OnChanged, () => changes++));
        cut.Find(".canvas-field").Click();
        cut.Find("[aria-label='Field width']").Change("2");
        Assert.Equal((3, 1, 2), (field.GridRow, field.GridColumn, field.GridColumnSpan));
        cut.Find("[aria-label='Field column']").Change("2");
        Assert.Equal(1, field.GridColumn);
        Assert.Equal("1", cut.Find("[aria-label='Field column']").GetAttribute("value"));
        Assert.Equal(1, changes);
        await cut.InvokeAsync(async () => Assert.True(await cut.Instance.CommitPlacement("First", 1, 5, 2, 2, false)));
        Assert.Equal((5, 2, 2), (field.GridRow, field.GridColumn, field.GridColumnSpan));
        Assert.Equal((3, 3, 2), (other.GridRow, other.GridColumn, other.GridColumnSpan));
        cut.Find(".section-heading").Click();
        cut.Find("[aria-label='Section columns']").Change("2");
        Assert.Equal(4, form.Sections[0].MaxColumns);
        cut.FindAll("button").Single(button => button.TextContent.Contains("Auto-arrange")).Click();
        Assert.Equal((1, 1), (other.GridRow, other.GridColumn));
        Assert.Equal((1, 3), (field.GridRow, field.GridColumn));
    }

    [Fact]
    public async Task PointerAddTargetsExactCellAndDelegatesCannotBypassRestrictions()
    {
        var form = new FormDefinition { Sections = new() { new() { Id = 1, MaxColumns = 4 }, new() { Id = 2, MaxColumns = 4, VisibilityCondition = "condition" } } };
        var cut = RenderComponent<DesignerWorkspace>(parameters => parameters.Add(component => component.Form, form)
            .Add(component => component.Columns, new() { new() { Name = "Country", DataType = "nvarchar", IsPrimaryKey = true } })
            .Add(component => component.OnAddField, column => form.Fields.Add(new() { Name = column.Name, FormSectionId = 1, GridColumnSpan = 1 })));
        cut.Find("[aria-label='Search available fields']").Input("nvarchar");
        Assert.Contains("PK", cut.Find(".available-field").TextContent);
        await cut.InvokeAsync(async () => Assert.True(await cut.Instance.CommitPlacement("Country", 2, 4, 3, 1, true)));
        var field = Assert.Single(form.Fields);
        Assert.Equal((2, 4, 3, 1), (field.FormSectionId, field.GridRow, field.GridColumn, field.GridColumnSpan));
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.PresentationOnly, true));
        await cut.InvokeAsync(async () => Assert.False(await cut.Instance.CommitPlacement("Country", 1, 1, 1, 1, false)));
        await cut.InvokeAsync(async () => Assert.False(await cut.Instance.CommitPlacement("New", 1, 1, 1, 1, true)));
        Assert.Equal(2, field.FormSectionId);
    }

    [Fact]
    public void UnsectionedFieldCanBeResizedAndMovedWithoutChangingOtherFields()
    {
        var field = new FormField { Name = "Legacy", DisplayName = "Legacy", GridColumnSpan = 6 };
        var form = new FormDefinition { Sections = new() { new() { Id = 1, MaxColumns = 2 } }, Fields = new() { field } };
        var cut = RenderComponent<DesignerWorkspace>(parameters => parameters.Add(component => component.Form, form));
        cut.FindAll(".outline-item").Single(button => button.TextContent == "Legacy").Click();
        cut.Find("[aria-label='Field width']").Change("2");
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.Form, form));
        Assert.Empty(cut.FindAll("[aria-label='Field row']"));
        cut.Find("[aria-label='Field section']").Change("1");
        Assert.Equal((1, 1, 1, 2), (field.FormSectionId, field.GridRow, field.GridColumn, field.GridColumnSpan));
    }

    [Fact]
    public void ExportBrowserFixtureWhenRequested()
    {
        var output = Environment.GetEnvironmentVariable("REQUESTR_DESIGNER_FIXTURE");
        if (string.IsNullOrEmpty(output)) return;
        var form = new FormDefinition { Name = "Countries", Sections = new() { new() { Id = 1, Name = "Details", MaxColumns = 4 }, new() { Id = 2, Name = "Additional", MaxColumns = 4 } },
            Fields = new() { new() { Name = "Country", DisplayName = "Country name", FormSectionId = 1, GridColumnSpan = 1 }, new() { Name = "Code", DisplayName = "Country code", FormSectionId = 1, GridColumn = 3, GridColumnSpan = 2 } } };
        var cut = RenderComponent<DesignerWorkspace>(parameters => parameters.Add(component => component.Form, form)
            .Add(component => component.Columns, new() { new() { Name = "Region", DataType = "nvarchar" } }));
        cut.Find(".canvas-field").Click();
        var web = StaticAssetTests.WebRoot;
        var site = new Uri(Path.Combine(web, "wwwroot/css/site.css")).AbsoluteUri;
        var css = new Uri(Path.Combine(web, "obj/Debug/net10.0/scopedcss/bundle/Requestr.Web.styles.css")).AbsoluteUri;
        var script = new Uri(Path.Combine(web, "wwwroot/js/designerGrid.js")).AbsoluteUri;
        File.WriteAllText(output, $$"""
            <!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1">
            <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.7/dist/css/bootstrap.min.css">
            <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.13.1/font/bootstrap-icons.min.css">
            <link rel="stylesheet" href="{{site}}"><link rel="stylesheet" href="{{css}}"></head><body>
            {{cut.Markup}}<script src="{{script}}"></script><script>
            window.commits = [];
            designerGrid.initialize(document.querySelector('.designer-workspace'), { invokeMethodAsync: (...args) => { window.commits.push(args); return Promise.resolve(true); } });
            </script></body></html>
            """);
    }

    [Fact]
    public void ConditionEditorOffersUnsectionedParentsAndWritesTypedJson()
    {
        string? saved = null;
        var cut = RenderComponent<ConditionEditor>(parameters => parameters.Add(component => component.Fields, new() { new() { Name = "Enabled", DisplayName = "Enabled", SqlDataType = "bit" } })
            .Add(component => component.ValueChanged, value => saved = value));
        cut.Find("select").Change("Enabled");
        cut.Find("select[id$='value']").Change("true");
        var condition = FormConditions.Parse(saved)!;
        Assert.Equal("Enabled", condition.Field);
        Assert.Equal(FormConditionOperator.Equals, condition.Operator);
        Assert.Equal("true", condition.Value);
    }
}