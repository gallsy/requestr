using System.ComponentModel.DataAnnotations;
using Requestr.Core.Models;
using Requestr.Core.Validation;
using Xunit;

namespace Requestr.Core.Tests.Models;

public class FormConditionTests
{
    [Theory]
    [InlineData("bit", "True", "1", true)]
    [InlineData("bit", "invalid", "invalid", false)]
    [InlineData("int", "001", "1", true)]
    [InlineData("varchar", "001", "1", false)]
    [InlineData("varchar", "AUS", "AUS", true)]
    public void ComparisonsUseTypedKeys(string type, string actual, string expected, bool result)
    {
        var form = new FormDefinition { Fields = new() { new() { Name = "Parent", SqlDataType = type } } };
        Assert.Equal(result, FormConditions.Matches(new() { Field = "Parent", Value = expected }, form, new Dictionary<string, object?> { ["Parent"] = actual }));
    }

    [Fact]
    public void InvalidTypesAndUnknownSectionParentsAreRejected()
    {
        var form = new FormDefinition { Fields = new() { new() { Name = "Enabled", SqlDataType = "bit" }, new() { Name = "Child" } } };
        form.Fields[1].VisibilityCondition = FormConditions.Serialize(new() { Field = "Enabled", Value = "invalid" });
        Assert.Throws<ValidationException>(() => FormConditions.ValidateConfiguration(form));
        Assert.False(FormConditions.Matches(new() { Field = "enabled", Operator = FormConditionOperator.NotEquals, Value = "true" }, form, new Dictionary<string, object?> { ["Enabled"] = "invalid" }));
        form.Fields[1].VisibilityCondition = null;
        form.Sections.Add(new() { VisibilityCondition = FormConditions.Serialize(new() { Field = "Missing", Operator = FormConditionOperator.IsEmpty }) });
        Assert.Throws<ValidationException>(() => FormConditions.ValidateConfiguration(form));
    }

    [Fact]
    public void EmptyIsNotNotEqualsAndCyclesAreRejected()
    {
        var form = new FormDefinition { Fields = new() { new() { Name = "Parent" }, new() { Name = "Child" } } };
        var condition = new FormCondition { Field = "Parent", Operator = FormConditionOperator.NotEquals, Value = "AUS" };
        Assert.False(FormConditions.Matches(condition, form, new Dictionary<string, object?>()));
        form.Fields[1].VisibilityCondition = FormConditions.Serialize(condition);
        FormConditions.ValidateConfiguration(form);
        form.Fields[0].VisibilityCondition = FormConditions.Serialize(new() { Field = "Child", Operator = FormConditionOperator.IsNotEmpty });
        Assert.Throws<ValidationException>(() => FormConditions.ValidateConfiguration(form));
    }

    [Fact]
    public void SectionConditionAppliesToChildrenAndCannotDependOnThem()
    {
        var form = new FormDefinition
        {
            Fields = new() { new() { Name = "Parent" }, new() { Name = "Child", FormSectionId = 1 } },
            Sections = new() { new() { Id = 1, VisibilityCondition = FormConditions.Serialize(new() { Field = "Parent", Value = "AUS" }) } }
        };
        FormConditions.ValidateConfiguration(form);
        Assert.False(FormConditions.IsApplicable(form, form.Fields[1], new Dictionary<string, object?>()));
        Assert.True(FormConditions.IsApplicable(form, form.Fields[1], new Dictionary<string, object?> { ["Parent"] = "AUS" }));
        form.Sections[0].VisibilityCondition = FormConditions.Serialize(new() { Field = "Child", Operator = FormConditionOperator.IsEmpty });
        Assert.Throws<ValidationException>(() => FormConditions.ValidateConfiguration(form));
    }
}