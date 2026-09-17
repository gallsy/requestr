using System.ComponentModel.DataAnnotations;
using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;
using Requestr.Core.Services;
using Xunit;

namespace Requestr.Core.Tests.Models;

public class FormDesignTests
{
    private static FormDefinition Form() => new()
    {
        Id = 1,
        DesignVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
        Sections = new() { new() { Id = 10, Name = "Details", MaxColumns = 12 } },
        Fields = new()
        {
            new() { Id = 20, Name = "Country", DisplayName = "Country", ControlType = "select", DropdownOptions = "UK\nUS", FormSectionId = 10 }
        }
    };

    [Fact]
    public void PresentationEditsAreAcceptedWithoutMutatingOriginal()
    {
        var current = Form();
        var update = UpdateFormDesignDto.FromForm(current);
        update.Fields[0].DisplayName = "Country name";
        update.Fields[0].HelpText = "Choose a country";
        update.Fields[0].DropdownOptions = "UK\nUS\nFR";
        update.Fields[0].GridRow = 2;
        update.Sections[0].Name = "Location";
        update.ValidateAgainst(current);
        Assert.Equal("Country", current.Fields[0].DisplayName);
        Assert.Equal("UK\nUS", current.Fields[0].DropdownOptions);
    }

    [Fact]
    public void WriteContractContainsOnlyAllowlistedProperties()
    {
        Assert.Equal(new[] { "Fields", "FormDefinitionId", "Sections", "Version" },
            typeof(UpdateFormDesignDto).GetProperties().Select(property => property.Name).Order());
        Assert.Equal(new[] { "DisplayName", "DisplayOrder", "DropdownOptions", "FormSectionId", "GridColumn", "GridColumnSpan", "GridRow", "HelpText", "Id" },
            typeof(FormDesignFieldDto).GetProperties().Select(property => property.Name).Order());
        Assert.Equal(new[] { "Description", "DisplayOrder", "Id", "MaxColumns", "Name" },
            typeof(FormDesignSectionDto).GetProperties().Select(property => property.Name).Order());
    }

    [Theory]
    [InlineData("added")]
    [InlineData("removed")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    public void AlteredFieldMembershipIsRejected(string change)
    {
        var current = Form();
        var update = UpdateFormDesignDto.FromForm(current);
        if (change == "removed") update.Fields.Clear();
        if (change == "added") update.Fields.Add(new() { Id = 99 });
        if (change == "duplicate") update.Fields.Add(update.Fields[0]);
        if (change == "foreign") update.Fields[0].Id = 99;
        Assert.Throws<ValidationException>(() => update.ValidateAgainst(current));
    }

    [Fact]
    public void StaleVersionIsRejected()
    {
        var current = Form();
        var update = UpdateFormDesignDto.FromForm(current);
        current.DesignVersion![7] = 2;
        Assert.Throws<InvalidOperationException>(() => update.ValidateAgainst(current));
    }

    [Fact]
    public void ChangingFormIdIsRejected()
    {
        var current = Form();
        var update = UpdateFormDesignDto.FromForm(current);
        update.FormDefinitionId = 2;
        Assert.Throws<InvalidOperationException>(() => update.ValidateAgainst(current));
    }

    [Fact]
    public void NewSectionsCanReceiveExistingFields()
    {
        var current = Form();
        var update = UpdateFormDesignDto.FromForm(current);
        update.Sections.Add(new() { Id = -1, Name = "New section", MaxColumns = 12 });
        update.Fields[0].FormSectionId = -1;
        update.ValidateAgainst(current);
    }

    [Fact]
    public void RemovingOrdinarySectionCanKeepFieldsUnassigned()
    {
        var current = Form();
        var update = UpdateFormDesignDto.FromForm(current);
        update.Sections.Clear();
        update.Fields[0].FormSectionId = null;
        update.ValidateAgainst(current);
    }

    [Fact]
    public void FieldsCannotBeMovedOutOfConditionalSection()
    {
        var current = Form();
        current.Sections[0].VisibilityCondition = "Restricted == true";
        var update = UpdateFormDesignDto.FromForm(current);
        update.Fields[0].FormSectionId = null;
        Assert.Throws<ValidationException>(() => update.ValidateAgainst(current));
    }

    [Fact]
    public void ConditionalSectionsCannotBeRemoved()
    {
        var current = Form();
        current.Sections[0].VisibilityCondition = "Restricted == true";
        var update = UpdateFormDesignDto.FromForm(current);
        update.Sections.Clear();
        update.Fields[0].FormSectionId = null;
        Assert.Throws<ValidationException>(() => update.ValidateAgainst(current));
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 13, 1)]
    [InlineData(1, 12, 2)]
    [InlineData(1, 1, 0)]
    public void InvalidGridPositionsAreRejected(int row, int column, int span)
    {
        var current = Form();
        var update = UpdateFormDesignDto.FromForm(current);
        update.Fields[0].GridRow = row;
        update.Fields[0].GridColumn = column;
        update.Fields[0].GridColumnSpan = span;
        Assert.Throws<ValidationException>(() => update.ValidateAgainst(current));
    }

    [Fact]
    public void UnknownSectionIsRejected()
    {
        var current = Form();
        var update = UpdateFormDesignDto.FromForm(current);
        update.Fields[0].FormSectionId = 99;
        Assert.Throws<ValidationException>(() => update.ValidateAgainst(current));
    }

    [Fact]
    public void OptionsOnNonChoiceFieldsCannotBeChanged()
    {
        var current = Form();
        current.Fields[0].ControlType = "text";
        var update = UpdateFormDesignDto.FromForm(current);
        update.Fields[0].DropdownOptions = "Unexpected";
        Assert.Throws<ValidationException>(() => update.ValidateAgainst(current));
    }

    [Fact]
    public void NewPermissionDoesNotReuseLegacyGrants()
    {
        Assert.Equal(40, (int)FormPermissionType.EditFormDesign);
        Assert.Empty(FormPermissionHelper.GetPrerequisitePermissions(FormPermissionType.EditFormDesign));
        Assert.Contains(FormPermissionType.EditFormDesign, FormPermissionHelper.GetPermissionsByCategory()["Administrative"]);
    }
}