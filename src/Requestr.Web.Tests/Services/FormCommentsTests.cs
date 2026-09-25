using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Services.FormRequests;
using Requestr.Core.Services.Workflow;
using Requestr.Web.Authorization;
using Requestr.Web.Components.FormBuilder;
using Requestr.Web.Pages;
using Xunit;

namespace Requestr.Web.Tests.Services;

public class FormCommentsTests : TestContext
{
    public FormCommentsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddBlazorBootstrap();
    }

    [Fact]
    public void DesignerDisablesHideWhenRequestCommentsAreRequired()
    {
        var form = new FormDefinition();
        var changes = 0;
        var cut = RenderComponent<FormDetailsTab>(parameters => parameters
            .Add(component => component.FormDefinition, form)
            .Add(component => component.OnChanged, () => changes++));
        var hide = cut.Find("#hideRequestCommentsSwitch");
        Assert.False(hide.HasAttribute("disabled"));
        Assert.False(form.HideRequestComments);
        hide.Change(true);
        Assert.True(form.HideRequestComments);
        cut.Find("#requiresRequestCommentsSwitch").Change(true);
        Assert.True(form.RequiresRequestComments);
        Assert.False(form.HideRequestComments);
        Assert.True(cut.Find("#hideRequestCommentsSwitch").HasAttribute("disabled"));
        cut.Find("#requiresRequestCommentsSwitch").Change(false);
        Assert.False(cut.Find("#hideRequestCommentsSwitch").HasAttribute("disabled"));
        Assert.False(form.HideRequestComments);
        Assert.Equal(3, changes);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void SubmissionShowsCommentsUnlessHiddenAndOptional(bool required, bool hidden, bool shown)
    {
        this.AddTestAuthorization().SetAuthorized("requester");
        var form = new FormDefinition
        {
            Id = 1, Name = "Request form", IsActive = true, TableName = "Records",
            DatabaseConnectionName = "ReferenceData", RequiresRequestComments = required, HideRequestComments = hidden
        };
        var definitions = new Mock<IFormDefinitionService>();
        definitions.Setup(service => service.GetByIdAsync(1)).ReturnsAsync(form);
        Services.AddSingleton(definitions.Object);
        var permissions = new Mock<IFormAuthorizationService>();
        permissions.Setup(service => service.UserHasPermissionAsync(It.IsAny<ClaimsPrincipal>(), 1, FormPermissionType.CreateRequest))
            .ReturnsAsync(true);
        Services.AddSingleton(permissions.Object);
        var database = new Mock<IDatabaseService>();
        database.Setup(service => service.GetTableColumnsAsync("ReferenceData", "Records", "dbo"))
            .ReturnsAsync(new List<ColumnInfo>());
        Services.AddSingleton(database.Object);
        Services.AddSingleton(Mock.Of<IDataService>());
        Services.AddSingleton(Mock.Of<IFormRequestQueryService>());
        Services.AddSingleton(Mock.Of<IFormRequestCommandService>());
        Services.AddSingleton(Mock.Of<IWorkflowDefinitionQueryService>());
        Services.AddSingleton(new Requestr.Web.Configuration.AppBrandingOptions());

        var cut = RenderComponent<FormSubmission>(parameters => parameters.Add(component => component.FormId, 1));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button[type='submit']")));
        Assert.Equal(shown, cut.FindAll("textarea[placeholder='Add any additional comments about this request...']").Count == 1);
    }
}