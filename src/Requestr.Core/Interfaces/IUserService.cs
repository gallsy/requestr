using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

public interface IUserService
{
    Task<User> UpsertFromClaimsAsync(string userObjectId, string? tenantId, string? displayName, string? email, string? upn);
    Task<User?> GetByObjectIdAsync(string userObjectId, string? tenantId);
}
