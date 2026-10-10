using System.Security.Claims;
using Assistant.Application;
using Assistant.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Assistant.Web.Session;

public sealed class UserSession(UserAccounts accounts)
{
    public const string TimeZoneHeader = "X-Time-Zone";

    private AppUser? _user;

    public async Task<AppUser> GetUserAsync(HttpContext http, CancellationToken cancellationToken)
    {
        if (_user is not null)
            return _user;

        Guid? claimedId = Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        var user = await accounts.GetOrCreateAsync(claimedId, cancellationToken);
        if (user.Id != claimedId)
            await SignInAsync(http, user);

        await accounts.SyncTimeZoneAsync(user, http.Request.Headers[TimeZoneHeader].ToString(), cancellationToken);
        return _user = user;
    }

    public static Task SignInAsync(HttpContext http, AppUser user)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
}
