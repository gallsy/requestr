namespace Requestr.Web.Services;

/// <summary>
/// Scoped service that holds the user's browser timezone for the duration of the Blazor circuit.
/// Initialized once via JS interop in MainLayout, then used by UiHelper for display conversions.
/// </summary>
public interface IUserTimezoneService
{
    /// <summary>The user's IANA timezone identifier (e.g. "Australia/Sydney"). Null if not yet initialized.</summary>
    string? TimezoneId { get; }

    /// <summary>The resolved TimeZoneInfo. Falls back to UTC if not yet initialized.</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>Whether the timezone has been successfully detected from the browser.</summary>
    bool IsInitialized { get; }

    /// <summary>Sets the timezone from the IANA identifier returned by the browser.</summary>
    void SetTimezone(string ianaTimezoneId);

    /// <summary>Converts a UTC DateTime to the user's local time.</summary>
    DateTime ToUserTime(DateTime utcDateTime);

    /// <summary>Formats a UTC DateTime using the user's timezone and the standard display format.</summary>
    string FormatDateTime(DateTime utcDateTime);

    /// <summary>Formats a UTC DateTime using the user's timezone and the date-only format.</summary>
    string FormatDate(DateTime utcDateTime);

    /// <summary>Formats a nullable UTC DateTime, returning a fallback if null.</summary>
    string FormatDateTime(DateTime? utcDateTime, string fallback = "");

    /// <summary>Formats a nullable UTC DateTime as date-only, returning a fallback if null.</summary>
    string FormatDate(DateTime? utcDateTime, string fallback = "");

    /// <summary>Formats a UTC DateTime using the user's timezone and the time-only format.</summary>
    string FormatTime(DateTime utcDateTime);
}

public class UserTimezoneService : IUserTimezoneService
{
    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;

    /// <summary>Date only: "Jan 15, 2026"</summary>
    private const string DateFormat = "MMM dd, yyyy";

    /// <summary>Date with time: "Jan 15, 2026 at 3:45 PM"</summary>
    private const string DateTimeFormat = "MMM dd, yyyy 'at' h:mm tt";

    /// <summary>Time only: "3:45 PM"</summary>
    private const string TimeFormat = "h:mm tt";

    public string? TimezoneId { get; private set; }
    public TimeZoneInfo TimeZone => _timeZone;
    public bool IsInitialized => TimezoneId != null;

    public void SetTimezone(string ianaTimezoneId)
    {
        TimezoneId = ianaTimezoneId;
        try
        {
            // .NET 6+ supports IANA timezone IDs on all platforms via TimeZoneInfo.FindSystemTimeZoneById
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaTimezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fallback: try to convert IANA to Windows timezone ID
            if (TryConvertIanaToWindows(ianaTimezoneId, out var windowsId))
            {
                _timeZone = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }
            else
            {
                // Last resort: stay on UTC
                _timeZone = TimeZoneInfo.Utc;
            }
        }
    }

    public DateTime ToUserTime(DateTime utcDateTime)
    {
        // Ensure we're treating the input as UTC
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, _timeZone);
    }

    public string FormatDateTime(DateTime utcDateTime)
        => ToUserTime(utcDateTime).ToString(DateTimeFormat);

    public string FormatDate(DateTime utcDateTime)
        => ToUserTime(utcDateTime).ToString(DateFormat);

    public string FormatDateTime(DateTime? utcDateTime, string fallback = "")
        => utcDateTime.HasValue ? FormatDateTime(utcDateTime.Value) : fallback;

    public string FormatDate(DateTime? utcDateTime, string fallback = "")
        => utcDateTime.HasValue ? FormatDate(utcDateTime.Value) : fallback;

    public string FormatTime(DateTime utcDateTime)
        => ToUserTime(utcDateTime).ToString(TimeFormat);

    /// <summary>
    /// Maps common IANA timezone IDs to Windows timezone IDs as a fallback
    /// for older runtimes or Windows containers that don't support IANA natively.
    /// </summary>
    private static bool TryConvertIanaToWindows(string ianaId, out string windowsId)
    {
        // Common mappings — .NET 8 on Linux/macOS handles IANA natively,
        // but Windows containers may still need Windows IDs.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Australia/Sydney"] = "AUS Eastern Standard Time",
            ["Australia/Melbourne"] = "AUS Eastern Standard Time",
            ["Australia/Brisbane"] = "E. Australia Standard Time",
            ["Australia/Perth"] = "W. Australia Standard Time",
            ["Australia/Adelaide"] = "Cen. Australia Standard Time",
            ["Australia/Darwin"] = "AUS Central Standard Time",
            ["Australia/Hobart"] = "Tasmania Standard Time",
            ["Europe/London"] = "GMT Standard Time",
            ["Europe/Paris"] = "Romance Standard Time",
            ["Europe/Berlin"] = "W. Europe Standard Time",
            ["America/New_York"] = "Eastern Standard Time",
            ["America/Chicago"] = "Central Standard Time",
            ["America/Denver"] = "Mountain Standard Time",
            ["America/Los_Angeles"] = "Pacific Standard Time",
            ["Asia/Tokyo"] = "Tokyo Standard Time",
            ["Asia/Singapore"] = "Singapore Standard Time",
            ["Asia/Kolkata"] = "India Standard Time",
            ["Pacific/Auckland"] = "New Zealand Standard Time",
            ["UTC"] = "UTC",
        };

        return map.TryGetValue(ianaId, out windowsId!);
    }
}
