using BlazorBootstrap;
using Requestr.Core.Interfaces;

namespace Requestr.Web.Services;

/// <summary>
/// Service for managing toast notifications using Blazor Bootstrap
/// </summary>
public class ToastNotificationService : IToastNotificationService
{
    private readonly ToastService _toastService;

    public ToastNotificationService(ToastService toastService)
    {
        _toastService = toastService;
    }

    public void ShowSuccess(string message, string? title = null)
    {
        var toastMessage = new ToastMessage
        {
            Type = ToastType.Success,
            Title = title ?? "Success",
            Message = message,
            AutoHide = true
        };
        _toastService.Notify(toastMessage);
    }

    public void ShowError(string message, string? title = null)
    {
        var toastMessage = new ToastMessage
        {
            Type = ToastType.Danger,
            Title = title ?? "Error",
            Message = message,
            AutoHide = false // Keep error messages visible until user dismisses
        };
        _toastService.Notify(toastMessage);
    }

    public void ShowWarning(string message, string? title = null)
    {
        var toastMessage = new ToastMessage
        {
            Type = ToastType.Warning,
            Title = title ?? "Warning",
            Message = message,
            AutoHide = false // Keep warning messages visible until user dismisses
        };
        _toastService.Notify(toastMessage);
    }

    public void ShowInfo(string message, string? title = null)
    {
        var toastMessage = new ToastMessage
        {
            Type = ToastType.Info,
            Title = title ?? "Information",
            Message = message,
            AutoHide = true
        };
        _toastService.Notify(toastMessage);
    }
}
