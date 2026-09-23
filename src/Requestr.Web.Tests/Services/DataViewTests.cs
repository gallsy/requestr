using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Web.Authorization;
using Requestr.Web.Configuration;
using Requestr.Web.Pages;
using Xunit;

namespace Requestr.Web.Tests.Services;

public class DataViewTests : TestContext
{
    [Fact]
    public void DisplaysLookupLabelsButDuplicatesRawKeys()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddBlazorBootstrap();
        this.AddTestAuthorization().SetAuthorized("viewer");
        var permissions = new Mock<IFormAuthorizationService>();
        permissions.Setup(service => service.UserHasPermissionAsync(It.IsAny<ClaimsPrincipal>(), 1, It.IsAny<FormPermissionType>()))
            .ReturnsAsync(true);
        Services.AddSingleton(permissions.Object);
        var form = new FormDefinition { Id = 1, Name = "Programs", Fields = new()
        {
            new() { Name = "Id", DisplayName = "Id", DataType = "int" },
            new() { Name = "ProgramId", DisplayName = "Program", DataType = "int" }
        } };
        var definitions = new Mock<IFormDefinitionService>();
        definitions.Setup(service => service.GetFormDefinitionAsync(1)).ReturnsAsync(form);
        Services.AddSingleton(definitions.Object);
        var result = new DataViewResult
        {
            Records = new() { new() { ["Id"] = 1, ["ProgramId"] = 12 }, new() { ["Id"] = 2, ["ProgramId"] = 99 } },
            Columns = new() { "Id", "ProgramId" }, PrimaryKeyColumns = new() { "Id" },
            TotalCount = 2, CurrentPage = 1, PageSize = 10, TotalPages = 1,
            LookupLabels = new() { ["ProgramId"] = new() { ["12"] = "Literacy & language" } }
        };
        var data = new Mock<IDataViewService>();
        data.Setup(service => service.GetDataAsync(1, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object?>?>(), It.IsAny<string?>(), It.IsAny<string>())).ReturnsAsync(result);
        Services.AddSingleton(data.Object);
        Services.AddSingleton(Mock.Of<IBulkFormRequestService>());
        Services.AddSingleton(new AppBrandingOptions());

        var cut = RenderComponent<DataView>(parameters => parameters.Add(component => component.FormDefinitionId, 1));
        cut.WaitForAssertion(() => Assert.Equal("Literacy & language", cut.Find("tbody tr:first-child td:last-child").TextContent));
        Assert.Equal("Literacy & language", cut.Find("tbody tr:first-child td:last-child").GetAttribute("title"));
        Assert.Equal("99", cut.Find("tbody tr:last-child td:last-child").TextContent);
        cut.Find("input[placeholder='Search...']").Input("Literacy");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Search").Click();
        data.Verify(service => service.GetDataAsync(1, 1, 10, "Literacy", null, null, "ASC"), Times.Once);
        cut.Find("th[data-column='ProgramId']").Click();
        data.Verify(service => service.GetDataAsync(1, 1, 10, "Literacy", null, "ProgramId", "ASC"), Times.Once);
        cut.Find("tbody tr:first-child button[title='Duplicate']").Click();
        var uri = Services.GetRequiredService<NavigationManager>().Uri;
        Assert.Contains("prefill_ProgramId=12", uri);
        Assert.DoesNotContain("Literacy", uri);
        Assert.Equal(12, Assert.IsType<int>(result.Records[0]["ProgramId"]));
    }
}