using System.Security.Claims;

namespace Requestr.Core.Utilities;

/// <summary>
/// Centralizes user identity extraction from ClaimsPrincipal.
/// Handles different claim types from Entra ID and other identity providers.
/// </summary>
public static class UserIdentityHelper
{
    /// <summary>
    /// Extracts the unique user identifier from the claims principal.
    /// Checks Entra ID object identifier, sub, NameIdentifier, and Identity.Name in order.
    /// </summary>
    public static string GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value ??
               user.FindFirst("oid")?.Value ??
               user.FindFirst("sub")?.Value ??
               user.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
               user.Identity?.Name ?? "Unknown";
    }

    /// <summary>
    /// Extracts the user's display name from the claims principal.
    /// Checks name, standard name claim, preferred_username, and UPN in order.
    /// </summary>
    public static string GetDisplayName(ClaimsPrincipal user)
    {
        return user.FindFirst("name")?.Value ??
               user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Value ??
               user.FindFirst("preferred_username")?.Value ??
               user.FindFirst("upn")?.Value ??
               user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn")?.Value ??
               user.Identity?.Name ?? "Unknown User";
    }

    /// <summary>
    /// Extracts the user's email address from the claims principal.
    /// Checks email claim, preferred_username, and UPN in order.
    /// </summary>
    public static string GetEmail(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Email)?.Value ??
               user.FindFirst("email")?.Value ??
               user.FindFirst("preferred_username")?.Value ??
               user.FindFirst("upn")?.Value ??
               user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn")?.Value ??
               user.Identity?.Name ?? "Unknown";
    }
}
