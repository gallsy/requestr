namespace Requestr.Web.Services;

/// <summary>
/// Interface for toast notification service
/// </summary>
public interface IToastNotificationService
{
    void ShowSuccess(string message, string? title = null);
    void ShowError(string message, string? title = null);
    void ShowWarning(string message, string? title = null);
    void ShowInfo(string message, string? title = null);
}
