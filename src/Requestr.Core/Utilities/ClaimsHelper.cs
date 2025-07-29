using System.Security.Claims;

namespace Requestr.Core.Utilities;

/// <summary>
/// Utility class for extracting information from user claims.
/// Centralizes the logic for handling different claim types from various identity providers.
/// </summary>
public static class ClaimsHelper
{
    /// <summary>
    /// Extracts user roles from claims principal.
    /// Handles multiple claim formats including standard ASP.NET Core roles and Entra ID app roles.
    /// </summary>
    /// <param name="user">The claims principal representing the authenticated user</param>
    /// <returns>A list of distinct role names</returns>
    public static List<string> GetUserRoles(ClaimsPrincipal user)
    {
        var roles = new List<string>();
        
        // Standard role claims (ClaimTypes.Role)
        roles.AddRange(user.FindAll(ClaimTypes.Role).Select(c => c.Value));
        
        // Entra ID app role claims (come in as "roles" claim type)
        roles.AddRange(user.FindAll("roles").Select(c => c.Value));
        
        // Alternative role claim (singular "role")
        roles.AddRange(user.FindAll("role").Select(c => c.Value));
        
        return roles.Distinct().ToList();
    }
}
