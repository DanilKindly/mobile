using System.Security.Claims;

namespace NETmessenger.Web.Security;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        var value =
            principal.FindFirstValue("user_id") ??
            principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
            principal.FindFirstValue("sub");

        return Guid.TryParse(value, out userId);
    }

    public static Guid GetRequiredUserId(this ClaimsPrincipal principal)
    {
        if (principal.TryGetUserId(out var userId))
        {
            return userId;
        }

        throw new UnauthorizedAccessException("Authenticated user id is missing.");
    }
}
