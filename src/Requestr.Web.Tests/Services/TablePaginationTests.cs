using Bunit;
using Requestr.Web.Components.Shared;
using Xunit;

namespace Requestr.Web.Tests.Services;

public class TablePaginationTests : TestContext
{
    [Fact]
    public void RaisesPageAndSizeChangesAndDisablesOutOfRangeNavigation()
    {
        int? page = null, size = null;
        var cut = RenderComponent<TablePagination>(parameters => parameters
            .Add(p => p.CurrentPage, 1)
            .Add(p => p.TotalPages, 3)
            .Add(p => p.PageSize, 10)
            .Add(p => p.TotalCount, 25)
            .Add(p => p.OnPageChanged, value => page = value)
            .Add(p => p.OnPageSizeChanged, value => size = value));

        Assert.Contains("1–10 of 25", cut.Markup);
        Assert.True(cut.Find("button[aria-label='Previous page']").HasAttribute("disabled"));
        cut.Find("button[aria-label='Next page']").Click();
        Assert.Equal(2, page);
        cut.Find("select").Change("25");
        Assert.Equal(25, size);
    }

    [Fact]
    public void FilterBarShowsSummaryAndClearsOnlyWhenActive()
    {
        var cleared = false;
        var cut = RenderComponent<FilterBar>(parameters => parameters
            .Add(p => p.Summary, "3 forms")
            .Add(p => p.OnClear, () => cleared = true));

        Assert.Contains("3 forms", cut.Markup);
        Assert.Empty(cut.FindAll("button"));
        cut.SetParametersAndRender(parameters => parameters.Add(p => p.ShowClear, true));
        cut.Find("button").Click();
        Assert.True(cleared);
    }
}
