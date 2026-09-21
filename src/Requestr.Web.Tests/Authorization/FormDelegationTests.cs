using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;
using Requestr.Core.Services.FormRequests;
using Requestr.Core.Services.Workflow;
using Requestr.Web.Components.FormBuilder;
using Requestr.Web.Configuration;
using Requestr.Web.Pages.Admin;
using Requestr.Web.Services;
using Xunit;

namespace Requestr.Web.Tests.Authorization;

public class FormDelegationTests : TestContext
{
    private readonly Mock<IFormDesignService> _design = new(MockBehavior.Strict);
    private readonly Mock<IFormDefinitionService> _definitions = new(MockBehavior.Strict);
    private readonly Mock<IDatabaseService> _database = new(MockBehavior.Strict);
    private readonly Mock<IFormPermissionService> _permissions = new(MockBehavior.Strict);
    private readonly EditorAuthentication _authentication = new();

    public FormDelegationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddBlazorBootstrap();
        Services.AddSingleton<AuthenticationStateProvider>(_authentication);
        Services.AddSingleton(_design.Object);
        Services.AddSingleton(_definitions.Object);
        Services.AddSingleton(_database.Object);
        Services.AddSingleton(_permissions.Object);
        Services.AddSingleton(Mock.Of<IDataService>());
        Services.AddSingleton(Mock.Of<IFormRequestQueryService>());
        var workflows = new Mock<IWorkflowDefinitionQueryService>();
        workflows.SetReturnsDefault(Task.FromResult(new List<WorkflowDefinition>()));
        Services.AddSingleton(workflows.Object);
        Services.AddSingleton(Mock.Of<IWorkflowDesignerService>());
        Services.AddSingleton(Mock.Of<IFormWorkflowConfigurationService>());
        Services.AddSingleton(Mock.Of<IToastNotificationService>());
        Services.AddSingleton(Mock.Of<IUserTimezoneService>());
        Services.AddSingleton(new AppBrandingOptions());
    }

    private static FormDefinition Design(int id = 1) => new()
    {
        Id = id, Name = "Countries", DesignVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
        Fields = new()
        {
            new() { Id = 20, Name = "Region", DisplayName = "Region", ControlType = "select", SqlDataType = "nvarchar", DropdownOptions = "North\nSouth", FormSectionId = 10, GridColumnSpan = 1 }
        },
        Sections = new() { new() { Id = 10, Name = "Details", MaxColumns = 2 } }
    };

    private void AllowForm(int id = 1) => _design.Setup(service => service.GetDesignAsync(It.IsAny<ClaimsPrincipal>(), id)).ReturnsAsync(Design(id));

    [Fact]
    public void DelegatedDesignerHasOnlyDesignerTabAndNoDatabasePalette()
    {
        AllowForm();
        var cut = RenderComponent<FormBuilder>(parameters => parameters.Add(component => component.FormId, 1));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("#formBuilderTabs button")));
        Assert.Equal("Form Designer", cut.Find("#formBuilderTabs button").TextContent.Trim());
        Assert.Empty(cut.FindAll(".field-palette-column"));
        Assert.Empty(cut.FindAll("button[title='Remove Field']"));
        _definitions.VerifyNoOtherCalls();
        _database.VerifyNoOtherCalls();
        _permissions.VerifyNoOtherCalls();
    }

    [Fact]
    public void DelegatedSaveUsesPresentationContractOnly()
    {
        AllowForm();
        UpdateFormDesignDto? submitted = null;
        _design.Setup(service => service.SaveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<UpdateFormDesignDto>()))
            .Callback<ClaimsPrincipal, UpdateFormDesignDto>((user, update) => submitted = update).Returns(Task.CompletedTask);
        var cut = RenderComponent<FormBuilder>(parameters => parameters.Add(component => component.FormId, 1));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".canvas-field")));
        cut.Find(".canvas-field").Click();
        var modal = cut.FindComponents<FieldConfigurationModal>().Single(component => component.Instance.Inline);
        modal.WaitForAssertion(() => Assert.NotEmpty(modal.FindAll("textarea")));
        Assert.DoesNotContain("Validation Regex", modal.Markup);
        Assert.DoesNotContain("Computed Value", modal.Markup);
        Assert.DoesNotContain("Read Only", modal.Markup);
        modal.Find("textarea").Change("North\nSouth\nWest");
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Save").Click();
        cut.WaitForAssertion(() => Assert.NotNull(submitted));
        Assert.Equal("North\nSouth\nWest", submitted!.Fields[0].DropdownOptions);
        Assert.Equal(1, submitted.FormDefinitionId);
        Assert.Equal(originalUri, navigation.Uri);
        Assert.Contains("Saved", cut.Find(".builder-savebar [role='status']").TextContent);
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Save and close").Click();
        cut.WaitForAssertion(() => Assert.EndsWith("/admin/forms", navigation.Uri));
        _definitions.VerifyNoOtherCalls();
        _database.VerifyNoOtherCalls();
        _permissions.VerifyNoOtherCalls();
    }

    [Fact]
    public void ChangingRouteRechecksAccessAndClearsPreviousForm()
    {
        AllowForm();
        _design.Setup(service => service.GetDesignAsync(It.IsAny<ClaimsPrincipal>(), 2))
            .ThrowsAsync(new UnauthorizedAccessException("Not allowed"));
        var cut = RenderComponent<FormBuilder>(parameters => parameters.Add(component => component.FormId, 1));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("#formBuilderTabs button")));
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.FormId, 2));
        cut.WaitForAssertion(() => Assert.Contains("Not allowed", cut.Markup));
        Assert.Empty(cut.FindAll("#formBuilderTabs"));
        Assert.True(cut.FindAll("button").Single(button => button.TextContent.Trim() == "Save").HasAttribute("disabled"));
    }

    [Fact]
    public void DelegatedUsersCannotOpenCreateRoute()
    {
        var cut = RenderComponent<FormBuilder>();
        cut.WaitForAssertion(() => Assert.Contains("You do not have permission", cut.Markup));
        Assert.Empty(cut.FindAll("#formBuilderTabs"));
        _design.VerifyNoOtherCalls();
        _definitions.VerifyNoOtherCalls();
        _database.VerifyNoOtherCalls();
    }

    [Fact]
    public void FormsListUsesAuthorizedQueryAndDoesNotExposeAdminActions()
    {
        var form = Design();
        form.IsActive = false;
        _design.Setup(service => service.GetEditableFormsAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(new List<FormDefinition> { form });
        var cut = RenderComponent<Forms>();
        cut.WaitForAssertion(() => Assert.Contains("Countries", cut.Markup));
        Assert.Contains("Inactive", cut.Markup);
        Assert.Single(cut.FindAll("tbody button"));
        Assert.Empty(cut.FindAll("button[title='Create New Form']"));
        Assert.DoesNotContain("All Roles", cut.Markup);
        _definitions.VerifyNoOtherCalls();
    }

    [Fact]
    public void AdminKeepsAllDesignerTabs()
    {
        _authentication.Role = "Admin";
        _definitions.Setup(service => service.GetByIdAsync(1)).ReturnsAsync(Design());
        _definitions.Setup(service => service.GetAllAsync()).ReturnsAsync(new List<FormDefinition>());
        _database.Setup(service => service.GetConnectionStringsAsync()).ReturnsAsync(new Dictionary<string, string>());
        var cut = RenderComponent<FormBuilder>(parameters => parameters.Add(component => component.FormId, 1));
        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll("#formBuilderTabs button").Count));
        Assert.Contains("Permissions", cut.Markup);
        Assert.Contains("Workflow", cut.Markup);
        Assert.Contains("Notifications", cut.Markup);
        cut.Find("input[placeholder='Enter form name']").Change("Renamed form");
        cut.WaitForAssertion(() => Assert.Contains("Unsaved changes", cut.Markup));
        _design.VerifyNoOtherCalls();
    }

    [Fact]
    public void AdminKeepsFormsListActions()
    {
        _authentication.Role = "Admin";
        _definitions.Setup(service => service.GetFormDefinitionsAsync()).ReturnsAsync(new List<FormDefinition> { Design() });
        var cut = RenderComponent<Forms>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("tbody button").Count));
        Assert.Single(cut.FindAll("button[title='Create New Form']"));
        Assert.Contains("All Roles", cut.Markup);
        _design.VerifyNoOtherCalls();
    }

    [Fact]
    public void DesignPermissionCanBeGrantedWithoutDataPermissions()
    {
        (string Role, FormPermissionType Permission, bool Granted)? change = null;
        var cut = RenderComponent<FormPermissionsTab>(parameters => parameters
            .Add(component => component.AvailableRoles, new List<string> { "Editors" })
            .Add(component => component.SelectedRole, "Editors")
            .Add(component => component.PermissionsByCategory, Requestr.Core.Services.FormPermissionHelper.GetPermissionsByCategory())
            .Add(component => component.OnPermissionChanged, value => change = value));
        var checkbox = cut.Find("#perm_Editors_40");
        Assert.False(checkbox.HasAttribute("disabled"));
        checkbox.Change(true);
        Assert.Equal(("Editors", FormPermissionType.EditFormDesign, true), change);
    }

    private sealed class EditorAuthentication : AuthenticationStateProvider
    {
        public string Role { get; set; } = "FormEditors";
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("roles", Role), new Claim("oid", "editor-id") }, "Test"))));
    }

    [Fact]
    public void UnsavedNavigationCanBeCancelled()
    {
        AllowForm();
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        var cut = RenderComponent<FormBuilder>(parameters => parameters.Add(component => component.FormId, 1));
        cut.Find(".canvas-field").Click();
        cut.Find(".field-properties input").Change("Changed label");
        cut.WaitForAssertion(() => Assert.Contains("Unsaved changes", cut.Markup));
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        cut.FindAll(".builder-savebar button").Single(button => button.TextContent.Trim() == "Close").Click();
        Assert.Equal(originalUri, navigation.Uri);
        Assert.Single(JSInterop.Invocations["confirm"]);
    }
}