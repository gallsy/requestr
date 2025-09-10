using System.Security.Claims;
using Requestr.Core.Interfaces;

namespace Requestr.Web.Services;

public class UserSyncMiddleware
{
    private readonly RequestDelegate _next;

    public UserSyncMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IUserService userService)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var oid = GetClaim(user, "oid", "http://schemas.microsoft.com/identity/claims/objectidentifier");
            if (!string.IsNullOrWhiteSpace(oid))
            {
                var tid = GetClaim(user, "tid", "http://schemas.microsoft.com/identity/claims/tenantid");
                var name = GetClaim(user, "name", ClaimTypes.Name);
                var email = GetClaim(user, "email", "emails");
                var upn = GetClaim(user, "upn", "preferred_username");

                // Prefer email from UPN if email claim is missing
                if (string.IsNullOrWhiteSpace(email))
                {
                    email = upn;
                }

                try
                {
                    await userService.UpsertFromClaimsAsync(oid!, tid, name, email, upn);
                }
                catch
                {
                    // Don't block requests if sync fails; log is handled by service logger
                }
            }
        }

        await _next(context);
    }

    private static string? GetClaim(ClaimsPrincipal user, params string[] types)
    {
        foreach (var type in types)
        {
            var value = user.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }
}
