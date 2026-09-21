using System.ComponentModel.DataAnnotations;
using Requestr.Core.Models;
using Requestr.Core.Validation;
using Xunit;

namespace Requestr.Core.Tests.Models;

public class LookupConfigurationTests
{
    [Theory]
    [InlineData("Category", "Category", true)]
    [InlineData("", "Category", false)]
    [InlineData("Category", "", false)]
    [InlineData("Department", "Category", false)]
    [InlineData("department", "Category", false)]
    public void LookupFilterLevelsRequireDistinctColumnsAndLabels(string column, string label, bool valid)
    {
        var field = new FormField
        {
            OptionSource = FieldOptionSource.DatabaseLookup, ControlType = "searchable-select",
            LookupSchema = "dbo", LookupTable = "Programs", LookupKeyColumn = "Id", LookupLabelColumn = "Name",
            LookupFilterLevels = new() { new() { Column = "Department", Label = "Department" }, new() { Column = column, Label = label } }
        };
        if (valid) LookupConfigurationValidator.Validate(field);
        else Assert.Throws<ValidationException>(() => LookupConfigurationValidator.Validate(field));
    }

    [Fact]
    public void ExistingFieldsDefaultToStaticOptions()
    {
        var field = new FormField();
        Assert.Equal(FieldOptionSource.Static, field.OptionSource);
        LookupConfigurationValidator.Validate(field);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    [InlineData(256)]
    public void LookupConnectionNameMustFitStoredLength(int length)
    {
        var field = new FormField
        {
            OptionSource = FieldOptionSource.DatabaseLookup, ControlType = "searchable-select",
            LookupSchema = "dbo", LookupTable = "Countries", LookupKeyColumn = "Id", LookupLabelColumn = "Name",
            LookupDatabaseConnectionName = new string('C', length)
        };
        if (length <= 255) LookupConfigurationValidator.Validate(field);
        else Assert.Throws<ValidationException>(() => LookupConfigurationValidator.Validate(field));
    }

    [Theory]
    [InlineData("UPDATE")]
    [InlineData("A&B-01")]
    [InlineData("001")]
    public void LookupKeysAreNotRewrittenOrComparedWithStaticOptions(string key)
    {
        var field = new FormField { OptionSource = FieldOptionSource.DatabaseLookup, ControlType = "searchable-select", DataType = "nvarchar" };
        Assert.True(InputValidator.ValidateInput(key, field).IsValid);
        Assert.Equal(key, InputValidator.SanitizeInput(key, field));
        field.OptionSource = FieldOptionSource.Static;
        Assert.False(InputValidator.ValidateInput(key, field).IsValid);
    }

    [Theory]
    [InlineData("searchable-select", "Id", true)]
    [InlineData("suggested-text", "Id", false)]
    [InlineData("select", "Id", false)]
    [InlineData("searchable-select", "", false)]
    public void LookupRequiresSearchableControlAndCompleteMapping(string control, string key, bool valid)
    {
        var field = new FormField
        {
            OptionSource = FieldOptionSource.DatabaseLookup, ControlType = control,
            LookupSchema = "dbo", LookupTable = "Countries", LookupKeyColumn = key, LookupLabelColumn = "Name"
        };
        if (valid)
            LookupConfigurationValidator.Validate(field);
        else
            Assert.Throws<ValidationException>(() => LookupConfigurationValidator.Validate(field));
    }
}