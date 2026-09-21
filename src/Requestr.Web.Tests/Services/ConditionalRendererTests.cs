using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Validation;
using Requestr.Web.Components;
using Requestr.Web.Components.Shared;
using Requestr.Web.Services;
using Xunit;

namespace Requestr.Web.Tests.Services;

public class ConditionalRendererTests : TestContext
{
    public ConditionalRendererTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void SectionsFollowValuesButRecordedReadOnlyValuesRemainVisible()
    {
        Services.AddLogging();
        Services.AddSingleton(Mock.Of<IWorkflowDesignerService>());
        var form = new FormDefinition
        {
            Fields = new() { new() { Name = "Region", DisplayName = "Region" }, new() { Name = "Notes", DisplayName = "Notes", FormSectionId = 2 } },
            Sections = new() { new() { Id = 1, Name = "Always" }, new() { Id = 2, Name = "Conditional notes", VisibilityCondition = FormConditions.Serialize(new() { Field = "Region", Value = "North" }) } }
        };
        var values = new Dictionary<string, object?> { ["Region"] = "South", ["Notes"] = "Recorded value" };
        var cut = RenderComponent<FormRenderer>(parameters => parameters.Add(component => component.FormDefinition, form).Add(component => component.FieldValues, values));
        Assert.DoesNotContain("Conditional notes", cut.Markup);
        values["Region"] = "North";
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.FieldValues, values));
        Assert.Contains("Conditional notes", cut.Markup);
        values["Region"] = "South";
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.DisplayMode, FormDisplayMode.ReadOnly));
        Assert.Contains("Recorded value", cut.Markup);
    }

    [Fact]
    public void ChangingParentClearsInvalidChildAndEmptyParentDisablesIt()
    {
        var service = new Mock<IFormLookupService>();
        service.Setup(lookup => lookup.ResolveDependentAsync(1, "Country", "1", "North", null, default)).ReturnsAsync(new LookupOption("1", "Country"));
        Services.AddSingleton(service.Object);
        string? value = "1";
        var cut = RenderComponent<FormFieldSelect>(parameters => parameters
            .Add(component => component.FormId, 1)
            .Add(component => component.Field, new FormField { Name = "Country", LookupParentField = "Region", OptionSource = FieldOptionSource.DatabaseLookup })
            .Add(component => component.Value, "1")
            .Add(component => component.ParentValue, "North")
            .Add(component => component.ValueChanged, changed => value = changed));
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.ParentValue, "South"));
        cut.WaitForAssertion(() => Assert.Null(value));
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.Value, (string?)null).Add(component => component.ParentValue, (string?)null));
        Assert.True(cut.Find("input").HasAttribute("disabled"));
    }
}