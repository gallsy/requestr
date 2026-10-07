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
using Requestr.Web.Services;
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
        cut.Find("input[aria-label='Search records']").Input("Literacy");
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

    private (Mock<IFormAuthorizationService> Permissions, Mock<IRecordHistoryService> History) SetupHistoryDataView()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddBlazorBootstrap();
        this.AddTestAuthorization().SetAuthorized("viewer");
        var permissions = new Mock<IFormAuthorizationService>();
        permissions.Setup(service => service.UserHasPermissionAsync(It.IsAny<ClaimsPrincipal>(), 1, It.IsAny<FormPermissionType>()))
            .ReturnsAsync(true);
        Services.AddSingleton(permissions.Object);
        var form = new FormDefinition { Id = 1, Name = "Countries", Fields = new()
        {
            new() { Name = "Id", DisplayName = "Id", DataType = "int" },
            new() { Name = "Name", DisplayName = "Country name", DataType = "text" }
        } };
        var definitions = new Mock<IFormDefinitionService>();
        definitions.Setup(service => service.GetFormDefinitionAsync(1)).ReturnsAsync(form);
        Services.AddSingleton(definitions.Object);
        var data = new Mock<IDataViewService>();
        data.Setup(service => service.GetDataAsync(1, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object?>?>(), It.IsAny<string?>(), It.IsAny<string>())).ReturnsAsync(new DataViewResult
        {
            Records = new() { new() { ["Id"] = 12, ["Name"] = "Aus" } },
            Columns = new() { "Id", "Name" }, PrimaryKeyColumns = new() { "Id" },
            TotalCount = 1, CurrentPage = 1, PageSize = 10, TotalPages = 1
        });
        Services.AddSingleton(data.Object);
        Services.AddSingleton(Mock.Of<IBulkFormRequestService>());
        Services.AddSingleton(Mock.Of<IFormLookupService>());
        Services.AddSingleton<IUserTimezoneService>(new UserTimezoneService());
        Services.AddSingleton(new AppBrandingOptions());
        var history = new Mock<IRecordHistoryService>();
        Services.AddSingleton(history.Object);
        return (permissions, history);
    }

    [Fact]
    public void HistoryActionListsRequestsFromFormsTheUserMayView()
    {
        var (permissions, history) = SetupHistoryDataView();
        permissions.Setup(service => service.UserHasPermissionAsync(It.IsAny<ClaimsPrincipal>(), 2, FormPermissionType.ViewRecordHistory))
            .ReturnsAsync(false);
        history.Setup(service => service.GetHistoryAsync(It.Is<FormDefinition>(form => form.Id == 1), """{"Id":"12"}""")).ReturnsAsync(new List<RecordHistoryEntry>
        {
            new() { FormRequestId = 7, FormDefinitionId = 1, FormName = "Countries", RequestType = RequestType.Update, Status = RequestStatus.Pending,
                RequestedByName = "Jo", RequestedAt = DateTime.UtcNow, Changes = new() { new("Name", "Australia", "Aus") } },
            new() { FormRequestId = 6, FormDefinitionId = 2, FormName = "Restricted", RequestType = RequestType.Update, Status = RequestStatus.Applied },
            new() { BulkFormRequestId = 3, BulkRowNumber = 4, FormDefinitionId = 1, FormName = "Countries", RequestType = RequestType.Insert,
                Status = RequestStatus.Applied, RequestedByName = "Sam", RequestedAt = DateTime.UtcNow.AddDays(-1), Changes = new() { new("Name", null, "Australia") } }
        });

        var cut = RenderComponent<DataView>(parameters => parameters.Add(component => component.FormDefinitionId, 1));
        cut.WaitForElement("tbody tr button[title='History']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Request #7", cut.Markup));
        var offcanvas = cut.Find(".offcanvas");
        Assert.Equal("Record history", offcanvas.QuerySelector(".offcanvas-header h5")!.TextContent);
        Assert.Equal("Id = 12", offcanvas.QuerySelector(".offcanvas-body > p")!.TextContent);
        Assert.Equal("/requests/7", cut.Find("a[href='/requests/7']").GetAttribute("href"));
        Assert.Equal("/bulk-requests/3", cut.Find("a[href='/bulk-requests/3']").GetAttribute("href"));
        Assert.DoesNotContain("Request #6", cut.Markup);
        Assert.DoesNotContain("Restricted", cut.Markup);
        Assert.Contains("1 request has not been applied to this record yet.", cut.Markup);
        Assert.Contains("Country name", cut.Markup);
    }

    [Fact]
    public void HistoryActionRequiresViewRecordHistoryPermission()
    {
        var (permissions, history) = SetupHistoryDataView();
        permissions.Setup(service => service.UserHasPermissionAsync(It.IsAny<ClaimsPrincipal>(), 1, FormPermissionType.ViewRecordHistory))
            .ReturnsAsync(false);

        var cut = RenderComponent<DataView>(parameters => parameters.Add(component => component.FormDefinitionId, 1));
        cut.WaitForElement("tbody tr button[title='Edit']");

        Assert.Empty(cut.FindAll("button[title='History']"));
        history.VerifyNoOtherCalls();
    }
}