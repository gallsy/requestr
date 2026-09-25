using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Services;
using Requestr.Core.Services.Workflow;
using Requestr.Web.Configuration;
using Requestr.Web.Services;
using Requestr.Web.Shared;
using Xunit;

namespace Requestr.Web.Tests.Services;

public class ApprovalBadgeTests : TestContext
{
    private readonly Mock<IWorkflowExecutionService> _workflows = new(MockBehavior.Strict);
    private readonly TestAuthorizationContext _authorization;
    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    public ApprovalBadgeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _authorization = this.AddTestAuthorization();
        _authorization.SetAuthorized("Approver");
        _authorization.SetRoles("Admin");
        _authorization.SetClaims(new Claim("oid", "approver-id"));
        Services.AddLogging();
        Services.AddBlazorBootstrap();
        Services.AddSingleton<ThemeService>();
        Services.AddSingleton(new AppBrandingOptions());
        Services.AddSingleton(Mock.Of<IFormDesignService>());
        Services.AddSingleton(Mock.Of<IUserTimezoneService>());
        Services.AddSingleton(_workflows.Object);
        SetPending(true);
    }

    [Fact]
    public async Task DesktopNavigationCollapsesAndExpandsIndependentlyOfMobile()
    {
        var layout = RenderComponent<MainLayout>();
        var collapse = layout.Find("#main-navigation button[aria-label='Collapse navigation']");
        Assert.Empty(layout.FindAll(".top-row button[aria-label='Collapse navigation']"));
        Assert.Equal("true", collapse.GetAttribute("aria-expanded"));
        collapse.Click();
        Assert.Equal("false", layout.Find("#main-navigation button[aria-label='Expand navigation']").GetAttribute("aria-expanded"));
        var links = layout.FindAll("#main-navigation a.sidebar-nav-link");
        Assert.Equal(7, links.Count);
        Assert.All(links, link =>
        {
            Assert.NotNull(link.QuerySelector(".sidebar-icon[aria-hidden='true']"));
            Assert.NotNull(link.QuerySelector(".sidebar-label"));
            Assert.False(string.IsNullOrWhiteSpace(link.GetAttribute("aria-label")));
            Assert.False(string.IsNullOrWhiteSpace(link.GetAttribute("title")));
        });
        Assert.Single(layout.FindAll("a[href='approvals'] .sidebar-approval-badge"));
        Assert.Equal(true, JSInterop.Invocations["requestrUi.setSidebarCollapsed"].Last().Arguments[0]);
        await layout.InvokeAsync(() => Navigation.NavigateTo("/forms"));
        Assert.Single(layout.FindAll("button[aria-label='Expand navigation']"));
        Assert.Contains("active", layout.Find("a[href='/forms']").ClassList);
        layout.Find("button[aria-label='Toggle navigation']").Click();
        Assert.Contains("sidebar-open", layout.Find(".page").ClassList);
        layout.Find("#main-navigation button[aria-label='Close navigation']").Click();
        Assert.DoesNotContain("sidebar-open", layout.Find(".page").ClassList);
        layout.Find("button[aria-label='Toggle navigation']").Click();
        layout.Find(".sidebar-overlay").Click();
        Assert.DoesNotContain("sidebar-open", layout.Find(".page").ClassList);
        layout.Find("button[aria-label='Expand navigation']").Click();
        Assert.Single(layout.FindAll("#main-navigation button[aria-label='Collapse navigation']"));
        Assert.Equal(false, JSInterop.Invocations["requestrUi.setSidebarCollapsed"].Last().Arguments[0]);
    }

    [Fact]
    public void DesktopNavigationRestoresSavedPreference()
    {
        JSInterop.Setup<bool>("requestrUi.isSidebarCollapsed").SetResult(true);
        var layout = RenderComponent<MainLayout>();
        Assert.Equal("false", layout.Find("#main-navigation button[aria-label='Expand navigation']").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void DesktopNavigationPrerendersCookiePreference()
    {
        JSInterop.Setup<bool>("requestrUi.isSidebarCollapsed").SetResult(true);
        var layout = RenderComponent<MainLayout>(parameters => parameters
            .AddCascadingValue(new UiPreferences(DarkMode: false, SidebarCollapsed: true)));
        Assert.Single(layout.FindAll("#main-navigation button[aria-label='Expand navigation']"));
    }

    private void SetPending(bool pending) => _workflows
        .Setup(service => service.GetPendingStepsForUserAsync("approver-id", It.IsAny<List<string>>()))
        .ReturnsAsync(pending ? new() { new WorkflowStepInstance() } : new List<WorkflowStepInstance>());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NavigationRechecksPendingApprovals(bool remaining)
    {
        var layout = RenderComponent<MainLayout>();
        Assert.Single(layout.FindAll("a[href='approvals'] .badge"));
        SetPending(remaining);
        await layout.InvokeAsync(() => Navigation.NavigateTo("/requests/1"));
        layout.WaitForAssertion(() => Assert.Equal(remaining ? 1 : 0, layout.FindAll("a[href='approvals'] .badge").Count));
        _workflows.Verify(service => service.GetPendingStepsForUserAsync("approver-id", It.IsAny<List<string>>()), Times.Exactly(2));
    }

    [Fact]
    public async Task OlderPendingResponseCannotRestoreClearedBadge()
    {
        var layout = RenderComponent<MainLayout>();
        var pending = new TaskCompletionSource<List<WorkflowStepInstance>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _workflows.SetupSequence(service => service.GetPendingStepsForUserAsync("approver-id", It.IsAny<List<string>>()))
            .Returns(pending.Task).ReturnsAsync(new List<WorkflowStepInstance>());
        await layout.InvokeAsync(() => Navigation.NavigateTo("/requests/1"));
        await layout.InvokeAsync(() => Navigation.NavigateTo("/approvals"));
        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll("a[href='approvals'] .badge")));
        var renderCount = layout.RenderCount;
        await layout.InvokeAsync(() => pending.SetResult(new() { new WorkflowStepInstance() }));
        layout.WaitForAssertion(() => Assert.True(layout.RenderCount > renderCount));
        Assert.Empty(layout.FindAll("a[href='approvals'] .badge"));
    }

    [Fact]
    public async Task AuthenticationChangesRefreshBadgeAndDisposalUnsubscribes()
    {
        var layout = RenderComponent<MainLayout>();
        Assert.Single(layout.FindAll("a[href='approvals'] .badge"));
        await layout.InvokeAsync(() => _authorization.SetNotAuthorized());
        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll("a[href='approvals'] .badge")));
        SetPending(false);
        await layout.InvokeAsync(() => _authorization.SetAuthorized("Approver"));
        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll("a[href='approvals'] .badge")));
        layout.Dispose();
        _workflows.Invocations.Clear();
        Navigation.NavigateTo("/approvals");
        _authorization.SetAuthorized("Approver");
        _workflows.VerifyNoOtherCalls();
    }
}