using System.Security.Claims;
using Moq;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;
using Requestr.Core.Repositories;
using Requestr.Core.Services;
using Xunit;

namespace Requestr.Core.Tests.Services;

public class FormDesignServiceTests
{
    private readonly Mock<IFormDesignRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IFormPermissionService> _permissions = new();
    private FormDesignService Service => new(_repository.Object, _permissions.Object);

    private static ClaimsPrincipal User(string role, string claimType = ClaimTypes.Role) => new(new ClaimsIdentity(
        new[] { new Claim(claimType, role), new Claim("oid", "editor-id") }, "Test"));

    [Fact]
    public async Task AnonymousUsersCannotReadListLoadOrSave()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }));
        Assert.False(await Service.CanEditAsync(user, 1));
        Assert.Empty(await Service.GetEditableFormsAsync(user));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.GetDesignAsync(user, 1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.SaveAsync(user, new() { FormDefinitionId = 1 }));
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AdminDoesNotRequireExplicitFormGrant()
    {
        Assert.True(await Service.CanEditAsync(User("Admin"), 1));
        _permissions.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(ClaimTypes.Role)]
    [InlineData("roles")]
    [InlineData("role")]
    public async Task DesignGrantUsesAppRolesAndIsScopedToForm(string claimType)
    {
        _permissions.Setup(service => service.HasPermissionAsync(1, "FormEditors", FormPermissionType.EditFormDesign)).ReturnsAsync(true);
        Assert.True(await Service.CanEditAsync(User("FormEditors", claimType), 1));
        Assert.False(await Service.CanEditAsync(User("FormEditors", claimType), 2));
    }

    [Fact]
    public async Task DataAccessDoesNotGrantEditing()
    {
        _permissions.Setup(service => service.HasPermissionAsync(1, "Readers", FormPermissionType.ViewData)).ReturnsAsync(true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.GetDesignAsync(User("Readers"), 1));
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ListPassesRolesWithoutGrantingAdminAccess()
    {
        _repository.Setup(repository => repository.GetEditableFormsAsync(
            It.Is<List<string>>(roles => roles.SequenceEqual(new[] { "FormEditors" })), false)).ReturnsAsync(new List<FormDefinition>());
        Assert.Empty(await Service.GetEditableFormsAsync(User("FormEditors")));
    }

    [Fact]
    public async Task RevocationAfterLoadPreventsSave()
    {
        _permissions.SetupSequence(service => service.HasPermissionAsync(1, "FormEditors", FormPermissionType.EditFormDesign))
            .ReturnsAsync(true).ReturnsAsync(false);
        _repository.Setup(repository => repository.GetAsync(1)).ReturnsAsync(new FormDefinition { Id = 1 });
        await Service.GetDesignAsync(User("FormEditors"), 1);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.SaveAsync(User("FormEditors"), new() { FormDefinitionId = 1 }));
        _repository.Verify(repository => repository.SaveAsync(It.IsAny<UpdateFormDesignDto>(), It.IsAny<List<string>>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SavePassesAuthenticatedIdentityForAuditAndPermissionRecheck()
    {
        var update = new UpdateFormDesignDto { FormDefinitionId = 1 };
        _permissions.Setup(service => service.HasPermissionAsync(1, "FormEditors", FormPermissionType.EditFormDesign)).ReturnsAsync(true);
        _repository.Setup(repository => repository.SaveAsync(update,
            It.Is<List<string>>(roles => roles.Contains("FormEditors")), false, "editor-id")).Returns(Task.CompletedTask);
        await Service.SaveAsync(User("FormEditors"), update);
        _repository.VerifyAll();
    }

    [Fact]
    public async Task MissingAuditIdentityPreventsSave()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }, "Test"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.SaveAsync(user, new() { FormDefinitionId = 1 }));
        _repository.VerifyNoOtherCalls();
    }
}