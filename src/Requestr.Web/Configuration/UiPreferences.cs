namespace Requestr.Web.Configuration;

/// <summary>
/// Initial UI state read from cookies written by wwwroot/js/ui-preferences.js,
/// so prerendered markup matches what the browser already painted.
/// </summary>
public sealed record UiPreferences(bool DarkMode, bool SidebarCollapsed)
{
    public const string ThemeCookie = "requestr_theme";
    public const string SidebarCookie = "requestr_sidebar";

    public static UiPreferences FromCookies(IRequestCookieCollection cookies) =>
        new(cookies[ThemeCookie] == "dark", cookies[SidebarCookie] == "collapsed");
}
