using Requestr.Core.Models;

namespace Requestr.Core.Services;

/// <summary>
/// Helper class for form permission management and descriptions
/// </summary>
public static class FormPermissionHelper
{
    /// <summary>
    /// Gets a human-readable description for a permission type
    /// </summary>
    public static string GetPermissionDescription(FormPermissionType permissionType)
    {
        return permissionType switch
        {
            FormPermissionType.CreateRequest => "Create new requests using this form",
            FormPermissionType.UpdateRequest => "Update existing records via requests",
            FormPermissionType.DeleteRequest => "Delete records via requests",
            FormPermissionType.ViewData => "View the data view page for this form",
            FormPermissionType.ViewDataDetails => "View detailed data records",
            FormPermissionType.BulkActions => "Perform bulk actions from the data view",
            FormPermissionType.BulkUploadCsv => "Upload CSV files for bulk operations",
            FormPermissionType.BulkEditRecords => "Edit multiple records at once",
            FormPermissionType.BulkDeleteRecords => "Delete multiple records at once",
            FormPermissionType.ViewAuditLog => "View audit logs for this form",
            FormPermissionType.ExportData => "Export data from this form",
            FormPermissionType.ManageFormSettings => "Modify form settings and configuration",
            _ => permissionType.ToString()
        };
    }

    /// <summary>
    /// Gets the category/group for a permission type
    /// </summary>
    public static string GetPermissionCategory(FormPermissionType permissionType)
    {
        return permissionType switch
        {
            FormPermissionType.CreateRequest => "Request Operations",
            FormPermissionType.UpdateRequest => "Request Operations", 
            FormPermissionType.DeleteRequest => "Request Operations",
            FormPermissionType.ViewData => "Data Access",
            FormPermissionType.ViewDataDetails => "Data Access",
            FormPermissionType.BulkActions => "Bulk Operations",
            FormPermissionType.BulkUploadCsv => "Bulk Operations",
            FormPermissionType.BulkEditRecords => "Bulk Operations",
            FormPermissionType.BulkDeleteRecords => "Bulk Operations",
            FormPermissionType.ViewAuditLog => "Administrative",
            FormPermissionType.ExportData => "Administrative",
            FormPermissionType.ManageFormSettings => "Administrative",
            _ => "Other"
        };
    }

    /// <summary>
    /// Gets all permission types grouped by category
    /// </summary>
    public static Dictionary<string, List<FormPermissionType>> GetPermissionsByCategory()
    {
        var permissions = Enum.GetValues<FormPermissionType>().ToList();
        
        return permissions
            .GroupBy(p => GetPermissionCategory(p))
            .ToDictionary(g => g.Key, g => g.OrderBy(p => (int)p).ToList());
    }

    /// <summary>
    /// Gets the icon class for a permission category
    /// </summary>
    public static string GetCategoryIcon(string category)
    {
        return category switch
        {
            "Request Operations" => "bi-plus-circle",
            "Data Access" => "bi-eye",
            "Bulk Operations" => "bi-layers",
            "Administrative" => "bi-gear",
            _ => "bi-question-circle"
        };
    }

    /// <summary>
    /// Gets the Bootstrap color class for a permission category
    /// </summary>
    public static string GetCategoryColorClass(string category)
    {
        return category switch
        {
            "Request Operations" => "text-success",
            "Data Access" => "text-info",
            "Bulk Operations" => "text-warning",
            "Administrative" => "text-danger",
            _ => "text-secondary"
        };
    }

    /// <summary>
    /// Determines if a permission is considered "dangerous" and should have extra confirmation
    /// </summary>
    public static bool IsDangerousPermission(FormPermissionType permissionType)
    {
        return permissionType switch
        {
            FormPermissionType.DeleteRequest => true,
            FormPermissionType.BulkDeleteRecords => true,
            FormPermissionType.ManageFormSettings => true,
            _ => false
        };
    }

    /// <summary>
    /// Gets permissions that depend on another permission (prerequisites)
    /// </summary>
    public static List<FormPermissionType> GetDependentPermissions(FormPermissionType permissionType)
    {
        return permissionType switch
        {
            FormPermissionType.ViewData => new List<FormPermissionType>
            {
                FormPermissionType.BulkActions,
                FormPermissionType.BulkUploadCsv,
                FormPermissionType.BulkEditRecords,
                FormPermissionType.BulkDeleteRecords,
                FormPermissionType.ViewDataDetails,
                FormPermissionType.ExportData
            },
            FormPermissionType.BulkActions => new List<FormPermissionType>
            {
                FormPermissionType.BulkUploadCsv,
                FormPermissionType.BulkEditRecords,
                FormPermissionType.BulkDeleteRecords
            },
            _ => new List<FormPermissionType>()
        };
    }

    /// <summary>
    /// Gets permissions that are required for this permission to work (prerequisites)
    /// </summary>
    public static List<FormPermissionType> GetPrerequisitePermissions(FormPermissionType permissionType)
    {
        return permissionType switch
        {
            FormPermissionType.BulkActions => new List<FormPermissionType> { FormPermissionType.ViewData },
            FormPermissionType.BulkUploadCsv => new List<FormPermissionType> { FormPermissionType.ViewData, FormPermissionType.BulkActions },
            FormPermissionType.BulkEditRecords => new List<FormPermissionType> { FormPermissionType.ViewData, FormPermissionType.BulkActions },
            FormPermissionType.BulkDeleteRecords => new List<FormPermissionType> { FormPermissionType.ViewData, FormPermissionType.BulkActions },
            FormPermissionType.ViewDataDetails => new List<FormPermissionType> { FormPermissionType.ViewData },
            FormPermissionType.ExportData => new List<FormPermissionType> { FormPermissionType.ViewData },
            _ => new List<FormPermissionType>()
        };
    }
}
