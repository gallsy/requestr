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