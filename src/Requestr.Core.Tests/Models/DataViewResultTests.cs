using Requestr.Core.Interfaces;
using Xunit;

namespace Requestr.Core.Tests.Models;

public class DataViewResultTests
{
    [Fact]
    public void LabelsAreDisplayOnlyAndMissingReferencesKeepTheirKeys()
    {
        var record = new Dictionary<string, object?> { ["ProgramId"] = 12, ["MissingId"] = 99, ["Empty"] = null };
        var result = new DataViewResult
        {
            Records = new() { record },
            LookupLabels = new() { ["ProgramId"] = new() { ["12"] = "Literacy" } }
        };
        Assert.Equal("Literacy", result.GetDisplayValue(record, "ProgramId"));
        Assert.Equal("99", result.GetDisplayValue(record, "MissingId"));
        Assert.Equal("", result.GetDisplayValue(record, "Empty"));
        Assert.Equal("", result.GetDisplayValue(record, "Absent"));
        Assert.Equal(12, Assert.IsType<int>(record["ProgramId"]));
    }
}