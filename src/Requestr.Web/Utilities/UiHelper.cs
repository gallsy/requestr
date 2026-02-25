using Requestr.Core.Models;
using Requestr.Web.Services;

namespace Requestr.Web.Utilities;

/// <summary>
/// Centralized UI helper for consistent badge classes, icons, labels, and date formatting
/// across all pages and components.
/// </summary>
public static class UiHelper
{
    // ==========================================
    // Date Format Constants
    // ==========================================

    /// <summary>Date only: "Jan 15, 2026"</summary>
    public const string DateFormat = "MMM dd, yyyy";

    /// <summary>Date with time: "Jan 15, 2026 at 3:45 PM"</summary>
    public const string DateTimeFormat = "MMM dd, yyyy 'at' h:mm tt";

    // ==========================================
    // Request Status — Badge Classes
    // ==========================================

    /// <summary>Returns a full Bootstrap badge class string (e.g. "bg-warning text-dark").</summary>
    public static string GetStatusBadgeClass(RequestStatus status) => status switch
    {
        RequestStatus.Pending  => "bg-warning text-dark",
        RequestStatus.Approved => "bg-success",
        RequestStatus.Rejected => "bg-danger",
        RequestStatus.Applied  => "bg-info",
        RequestStatus.Failed   => "bg-danger",
        _ => "bg-secondary"
    };

    /// <summary>Returns a bare Bootstrap color name (e.g. "warning", "success").</summary>
    public static string GetStatusColor(RequestStatus status) => status switch
    {
        RequestStatus.Pending  => "warning",
        RequestStatus.Approved => "success",
        RequestStatus.Rejected => "danger",
        RequestStatus.Applied  => "info",
        RequestStatus.Failed   => "danger",
        _ => "secondary"
    };

    /// <summary>Returns a Bootstrap icon class for the status.</summary>
    public static string GetStatusIcon(RequestStatus status) => status switch
    {
        RequestStatus.Pending  => "bi-hourglass-split",
        RequestStatus.Approved => "bi-check-circle-fill",
        RequestStatus.Rejected => "bi-x-circle-fill",
        RequestStatus.Applied  => "bi-check2-all",
        RequestStatus.Failed   => "bi-exclamation-triangle-fill",
        _ => "bi-info-circle"
    };

    /// <summary>Returns a Bootstrap text color class for the status icon.</summary>
    public static string GetStatusIconColor(RequestStatus status) => status switch
    {
        RequestStatus.Pending  => "text-warning",
        RequestStatus.Approved => "text-success",
        RequestStatus.Rejected => "text-danger",
        RequestStatus.Applied  => "text-info",
        RequestStatus.Failed   => "text-danger",
        _ => "text-muted"
    };

    /// <summary>Returns a subtle background class for status sections.</summary>
    public static string GetStatusBackgroundClass(RequestStatus status) => status switch
    {
        RequestStatus.Pending  => "bg-warning bg-opacity-10",
        RequestStatus.Approved => "bg-success bg-opacity-10",
        RequestStatus.Rejected => "bg-danger bg-opacity-10",
        RequestStatus.Applied  => "bg-info bg-opacity-10",
        RequestStatus.Failed   => "bg-danger bg-opacity-10",
        _ => "bg-light"
    };

    /// <summary>Returns a short human-readable title for the status.</summary>
    public static string GetStatusTitle(RequestStatus status, bool hasWorkflow = false) => status switch
    {
        RequestStatus.Pending when hasWorkflow => "Awaiting Approval",
        RequestStatus.Pending  => "Processing",
        RequestStatus.Approved => "Approved",
        RequestStatus.Rejected => "Rejected",
        RequestStatus.Applied  => "Complete",
        RequestStatus.Failed   => "Failed",
        _ => "Status"
    };

    /// <summary>Returns a descriptive message explaining the status.</summary>
    public static string GetStatusMessage(RequestStatus status, bool hasWorkflow = false) => status switch
    {
        RequestStatus.Pending when hasWorkflow =>
            "This request is awaiting approval.",
        RequestStatus.Pending =>
            "This request is being processed.",
        RequestStatus.Approved =>
            "This request has been approved and the changes have been applied.",
        RequestStatus.Rejected =>
            "This request was rejected and no changes were made.",
        RequestStatus.Applied =>
            "This request has been completed and the changes are now in the database.",
        RequestStatus.Failed =>
            "This request failed to apply. An administrator can retry the operation.",
        _ => ""
    };

    // ==========================================
    // Request Type — Badge Classes
    // ==========================================

    /// <summary>Returns a full Bootstrap badge class string for request type.</summary>
    public static string GetRequestTypeBadgeClass(RequestType requestType) => requestType switch
    {
        RequestType.Insert => "bg-success",
        RequestType.Update => "bg-primary",
        RequestType.Delete => "bg-danger",
        _ => "bg-secondary"
    };

    /// <summary>Returns a bare Bootstrap color name for request type.</summary>
    public static string GetRequestTypeColor(RequestType requestType) => requestType switch
    {
        RequestType.Insert => "success",
        RequestType.Update => "primary",
        RequestType.Delete => "danger",
        _ => "secondary"
    };

    /// <summary>Returns a Bootstrap icon class for the request type.</summary>
    public static string GetRequestTypeIcon(RequestType requestType) => requestType switch
    {
        RequestType.Insert => "bi-plus-circle-fill",
        RequestType.Update => "bi-pencil-fill",
        RequestType.Delete => "bi-trash-fill",
        _ => "bi-file-earmark"
    };

    /// <summary>Returns a CSS class for request type styling.</summary>
    public static string GetRequestTypeClass(RequestType requestType) => requestType switch
    {
        RequestType.Insert => "request-type-insert",
        RequestType.Update => "request-type-update",
        RequestType.Delete => "request-type-delete",
        _ => "request-type-default"
    };

    /// <summary>Returns a human-readable label for individual requests.</summary>
    public static string GetRequestTypeLabel(RequestType requestType) => requestType switch
    {
        RequestType.Insert => "New Record",
        RequestType.Update => "Update Record",
        RequestType.Delete => "Delete Record",
        _ => requestType.ToString()
    };

    /// <summary>Returns a human-readable label for bulk requests.</summary>
    public static string GetBulkRequestTypeLabel(RequestType requestType) => requestType switch
    {
        RequestType.Insert => "Bulk Insert",
        RequestType.Update => "Bulk Update",
        RequestType.Delete => "Bulk Delete",
        _ => requestType.ToString()
    };

    // ==========================================
    // Workflow Step Status
    // ==========================================

    /// <summary>Returns a bare color name for workflow step instance status.</summary>
    public static string GetStepStatusColor(WorkflowStepInstanceStatus status) => status switch
    {
        WorkflowStepInstanceStatus.Pending    => "warning",
        WorkflowStepInstanceStatus.InProgress => "info",
        WorkflowStepInstanceStatus.Completed  => "success",
        WorkflowStepInstanceStatus.Failed     => "danger",
        WorkflowStepInstanceStatus.Skipped    => "secondary",
        _ => "secondary"
    };

    // ==========================================
    // History / Change Type
    // ==========================================

    /// <summary>Returns a badge class for form request history change types.</summary>
    public static string GetHistoryMarkerColor(FormRequestChangeType changeType) => changeType switch
    {
        FormRequestChangeType.Created               => "bg-primary",
        FormRequestChangeType.Updated               => "bg-info",
        FormRequestChangeType.StatusChanged          => "bg-warning",
        FormRequestChangeType.Approved               => "bg-success",
        FormRequestChangeType.Rejected               => "bg-danger",
        FormRequestChangeType.Applied                => "bg-success",
        FormRequestChangeType.Failed                 => "bg-danger",
        FormRequestChangeType.WorkflowStarted        => "bg-primary",
        FormRequestChangeType.WorkflowStepCompleted  => "bg-info",
        FormRequestChangeType.WorkflowStepApproved   => "bg-success",
        FormRequestChangeType.WorkflowStepRejected   => "bg-danger",
        FormRequestChangeType.WorkflowCompleted      => "bg-success",
        _ => "bg-secondary"
    };

    // ==========================================
    // Relative Time
    // ==========================================

    /// <summary>Formats a UTC DateTime as a relative time string (e.g. "5m ago", "2d ago").</summary>
    public static string FormatRelativeTime(DateTime utcDateTime)
    {
        var ts = DateTime.UtcNow - utcDateTime;
        if (ts.TotalSeconds < 60)  return "just now";
        if (ts.TotalMinutes < 60)  return $"{(int)ts.TotalMinutes}m ago";
        if (ts.TotalHours < 24)    return $"{(int)ts.TotalHours}h ago";
        if (ts.TotalDays < 7)      return $"{(int)ts.TotalDays}d ago";
        if (ts.TotalDays < 30)     return $"{(int)(ts.TotalDays / 7)}w ago";
        if (ts.TotalDays < 365)    return $"{(int)(ts.TotalDays / 30)}mo ago";
        return $"{(int)(ts.TotalDays / 365)}y ago";
    }

    // ==========================================
    // Timezone-Aware Display Formatting
    // ==========================================

    /// <summary>Converts a UTC DateTime to the user's local time and formats with date + time.</summary>
    public static string FormatUserDateTime(DateTime utcDateTime, IUserTimezoneService tz)
        => tz.FormatDateTime(utcDateTime);

    /// <summary>Converts a UTC DateTime to the user's local time and formats with date only.</summary>
    public static string FormatUserDate(DateTime utcDateTime, IUserTimezoneService tz)
        => tz.FormatDate(utcDateTime);

    /// <summary>Converts a nullable UTC DateTime to the user's local time and formats with date + time.</summary>
    public static string FormatUserDateTime(DateTime? utcDateTime, IUserTimezoneService tz, string fallback = "")
        => tz.FormatDateTime(utcDateTime, fallback);

    /// <summary>Converts a nullable UTC DateTime to the user's local time and formats with date only.</summary>
    public static string FormatUserDate(DateTime? utcDateTime, IUserTimezoneService tz, string fallback = "")
        => tz.FormatDate(utcDateTime, fallback);
}
