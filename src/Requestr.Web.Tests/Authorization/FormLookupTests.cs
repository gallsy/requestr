using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Moq;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Services.FormRequests;
using Requestr.Core.Services.Workflow;
using Requestr.Web.Authorization;
using Requestr.Web.Services;
using Xunit;

namespace Requestr.Web.Tests.Authorization;

public class FormLookupTests
{
    private readonly Mock<IFormAuthorizationService> _authorization = new();
    private readonly Mock<IFormDefinitionService> _definitions = new();
    private readonly Mock<IFormRequestQueryService> _requests = new();
    private readonly Mock<IWorkflowInstanceService> _workflows = new();
    private readonly Mock<IBulkFormRequestService> _bulk = new();
    private readonly Mock<ILookupDataService> _data = new(MockBehavior.Strict);
    private readonly LookupAuthentication _authentication = new();
    private readonly FormDefinition _form = new()
    {
        Id = 1, Fields = new() { new() { Name = "CountryId", OptionSource = FieldOptionSource.DatabaseLookup, LookupDatabaseConnectionName = "ExternalReferenceData" } }
    };
    private FormLookupService Service => new(_authentication, _authorization.Object, _definitions.Object,
        _requests.Object, _workflows.Object, _data.Object, _bulk.Object);

    public FormLookupTests()
    {
        _definitions.Setup(service => service.GetFormDefinitionAsync(1)).ReturnsAsync(_form);
        _data.Setup(service => service.SearchAsync(_form, _form.Fields[0], It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new LookupOption("1", "Country") });
        _data.Setup(service => service.ResolveAsync(_form, _form.Fields[0], "1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LookupOption("1", "Country"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnonymousAndDesignOnlyUsersCannotReadLookupData(bool authenticated)
    {
        _authentication.Authenticated = authenticated;
        _authentication.Role = "Editors";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.SearchAsync(1, "CountryId", ""));
        _data.VerifyNoOtherCalls();
        _definitions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FormGrantIsRecheckedAndDoesNotIncludeDesignPermission()
    {
        _authorization.Setup(service => service.UserHasAnyPermissionAsync(It.IsAny<ClaimsPrincipal>(), 1, It.IsAny<FormPermissionType[]>()))
            .ReturnsAsync(true);
        Assert.Single(await Service.SearchAsync(1, "CountryId", "Country"));
        _authorization.Verify(service => service.UserHasAnyPermissionAsync(It.IsAny<ClaimsPrincipal>(), 1,
            It.Is<FormPermissionType[]>(permissions => !permissions.Contains(FormPermissionType.EditFormDesign))), Times.Once);
        _authorization.Reset();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.SearchAsync(1, "CountryId", "Country"));
        _data.Verify(service => service.SearchAsync(It.IsAny<FormDefinition>(), It.IsAny<FormField>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdminCanSearchOnlySavedLookupFields()
    {
        _authentication.Role = "Admin";
        Assert.Single(await Service.SearchAsync(1, "CountryId", ""));
        _data.Verify(service => service.SearchAsync(_form,
            It.Is<FormField>(field => field.LookupDatabaseConnectionName == "ExternalReferenceData"), "", It.IsAny<CancellationToken>()), Times.Once);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.SearchAsync(1, "SecretColumn", ""));
        _form.Fields[0].OptionSource = FieldOptionSource.Static;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.SearchAsync(1, "CountryId", ""));
    }

    [Fact]
    public async Task RequestOwnerCanResolveButCannotUseAnotherFormsRequest()
    {
        _requests.Setup(service => service.GetByIdAsync(10)).ReturnsAsync(new FormRequest { FormDefinitionId = 1, RequestedBy = "user-id" });
        Assert.Equal("Country", (await Service.ResolveAsync(1, "CountryId", "1", 10))!.Text);
        _requests.Setup(service => service.GetByIdAsync(10)).ReturnsAsync(new FormRequest { FormDefinitionId = 2, RequestedBy = "user-id" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.ResolveAsync(1, "CountryId", "1", 10));
    }

    [Fact]
    public async Task AssignedWorkflowParticipantCanResolve()
    {
        _requests.Setup(service => service.GetByIdAsync(10)).ReturnsAsync(new FormRequest { FormDefinitionId = 1, RequestedBy = "other", WorkflowInstanceId = 30 });
        _workflows.Setup(service => service.HasUserParticipatedInWorkflowAsync("user-id", It.IsAny<List<string>>(), 30)).ReturnsAsync(true);
        Assert.NotNull(await Service.ResolveAsync(1, "CountryId", "1", 10));
    }

    [Fact]
    public async Task BulkOwnerCanResolveWithoutFormGrant()
    {
        _bulk.Setup(service => service.GetBulkFormRequestByIdAsync(20)).ReturnsAsync(new BulkFormRequest { FormDefinitionId = 1, RequestedBy = "user-id" });
        Assert.NotNull(await Service.ResolveAsync(1, "CountryId", "1", bulkRequestId: 20));
    }

    private sealed class LookupAuthentication : AuthenticationStateProvider
    {
        public bool Authenticated { get; set; } = true;
        public string Role { get; set; } = "User";
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", "user-id"), new Claim("roles", Role) }, Authenticated ? "test" : null))));
    }
}