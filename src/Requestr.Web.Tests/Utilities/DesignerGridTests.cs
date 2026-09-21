using Requestr.Core.Models;
using Requestr.Web.Utilities;
using Xunit;

namespace Requestr.Web.Tests.Utilities;

public class DesignerGridTests
{
    [Fact]
    public void PrecisePlacementPreservesGapsAndRejectsCollisionsWithoutMutation()
    {
        var section = new FormSection { Id = 1, MaxColumns = 6 };
        var field = new FormField { Name = "First", FormSectionId = 1, GridRow = 3, GridColumn = 2, GridColumnSpan = 2 };
        var other = new FormField { Name = "Second", FormSectionId = 1, GridRow = 3, GridColumn = 5, GridColumnSpan = 2 };
        var form = new FormDefinition { Sections = new() { section }, Fields = new() { field, other } };
        Assert.True(DesignerGrid.TryPlace(form, field, section, 3, 2, 3, false));
        Assert.False(DesignerGrid.TryPlace(form, field, section, 3, 2, 4, false));
        Assert.False(DesignerGrid.TryPlace(form, field, section, 3, 6, 2, false));
        Assert.False(DesignerGrid.TryPlace(form, field, section, 1001, 1, 1, false));
        Assert.Equal((3, 2, 3), (field.GridRow, field.GridColumn, field.GridColumnSpan));
        Assert.Equal((3, 5, 2), (other.GridRow, other.GridColumn, other.GridColumnSpan));
        Assert.False(DesignerGrid.CanSetColumns(form, section, 4));
        Assert.True(DesignerGrid.Arrange(form, section));
        Assert.Equal((1, 1, 3), (field.GridRow, field.GridColumn, field.GridColumnSpan));
        Assert.Equal((1, 4, 2), (other.GridRow, other.GridColumn, other.GridColumnSpan));
    }

    [Fact]
    public void DelegatesCannotCrossDifferentConditionsOrAddFields()
    {
        var source = new FormSection { Id = 1, MaxColumns = 6 };
        var target = new FormSection { Id = 2, MaxColumns = 6, VisibilityCondition = "condition" };
        var field = new FormField { Name = "First", FormSectionId = 1, GridColumnSpan = 2 };
        var form = new FormDefinition { Sections = new() { source, target }, Fields = new() { field } };
        Assert.False(DesignerGrid.TryPlace(form, field, target, 1, 1, 2, true));
        Assert.False(DesignerGrid.CanPlace(form, null, source, 2, 1, 1, true));
        target.VisibilityCondition = "";
        Assert.True(DesignerGrid.TryPlace(form, field, target, 4, 3, 2, true));
        Assert.Equal(2, field.FormSectionId);
    }
}