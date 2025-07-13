# Form Permission Security Implementation

## Overview

This document describes how to use the comprehensive form permission security system that has been implemented. The system provides **granular, role-based permissions** for each form, where admins can dynamically define roles and assign specific permissions.

## Security Components

### 1. **Authorization Handlers**
- `FormPermissionHandler`: Checks permissions using resource-based authorization
- `FormPermissionPolicyHandler`: Checks permissions using policy-based authorization

### 2. **Custom Services**
- `IFormAuthorizationService`: Service for checking permissions in code
- `IDynamicAuthorizationPolicyProvider`: Service for creating dynamic policies

### 3. **Blazor Components**
- `DynamicAuthorizeView`: Component for form-specific permission checks
- `PolicyAuthorizeView`: Component for policy-based authorization

### 4. **Attributes**
- `FormPermissionAttribute`: Attribute for controller/page authorization
- `RequireAnyFormPermissionAttribute`: Attribute requiring any of multiple permissions

## Usage Examples

### 1. **Using in Blazor Pages/Components**

#### Option A: Using DynamicAuthorizeView Component
```razor
@* Show content only if user can create requests for form 123 *@
<DynamicAuthorizeView FormDefinitionId="123" Permission="FormPermissionType.CreateRequest">
    <button class="btn btn-primary">Create New Request</button>
</DynamicAuthorizeView>

@* Show content if user has ANY of these permissions *@
<DynamicAuthorizeView FormDefinitionId="123" AnyPermissions="new[] { FormPermissionType.CreateRequest, FormPermissionType.UpdateRequest }">
    <button class="btn btn-success">Submit Request</button>
</DynamicAuthorizeView>

@* Show content if user has ALL of these permissions *@
<DynamicAuthorizeView FormDefinitionId="123" AllPermissions="new[] { FormPermissionType.ViewData, FormPermissionType.ExportData }">
    <button class="btn btn-info">Export Data</button>
</DynamicAuthorizeView>

@* With fallback content for unauthorized users *@
<DynamicAuthorizeView FormDefinitionId="123" Permission="FormPermissionType.BulkUploadCsv">
    <button class="btn btn-warning">Upload CSV</button>
    <NotAuthorized>
        <div class="alert alert-warning">You don't have permission to upload CSV files.</div>
    </NotAuthorized>
</DynamicAuthorizeView>
```

#### Option B: Using Code-Behind Permission Checks
```csharp
@inject IFormAuthorizationService FormAuthService
@inject AuthenticationStateProvider AuthStateProvider

@code {
    private bool _canCreateRequest = false;
    private bool _canViewData = false;
    
    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;
        
        _canCreateRequest = await FormAuthService.UserHasPermissionAsync(
            user, formId, FormPermissionType.CreateRequest);
            
        _canViewData = await FormAuthService.UserHasPermissionAsync(
            user, formId, FormPermissionType.ViewData);
    }
}

@if (_canCreateRequest)
{
    <button class="btn btn-primary">Create Request</button>
}

@if (_canViewData)
{
    <a href="/data/view/@formId" class="btn btn-info">View Data</a>
}
```

### 2. **Using in Controllers/API Endpoints**

#### Option A: Using Custom Attributes
```csharp
[FormPermission(FormPermissionType.CreateRequest, "formId")]
public async Task<IActionResult> CreateRequest(int formId, [FromBody] RequestData data)
{
    // Only users with CreateRequest permission for this form can access this
    // formId is automatically extracted from route parameter
    return Ok();
}

[RequireAnyFormPermission(FormPermissionType.ViewData, FormPermissionType.ViewDataDetails)]
public async Task<IActionResult> GetFormData(int formId)
{
    // Users need either ViewData OR ViewDataDetails permission
    return Ok();
}
```

#### Option B: Using Manual Authorization Checks
```csharp
public async Task<IActionResult> BulkUpload(int formId, IFormFile file)
{
    // Manual permission check
    var resource = new FormPermissionResource(formId, FormPermissionType.BulkUploadCsv);
    var requirement = new FormPermissionRequirement(FormPermissionType.BulkUploadCsv);
    
    var authResult = await _authorizationService.AuthorizeAsync(User, resource, requirement);
    if (!authResult.Succeeded)
    {
        return Forbid();
    }
    
    // Process the upload...
    return Ok();
}
```

#### Option C: Using FormAuthorizationService
```csharp
public async Task<IActionResult> DeleteRequest(int formId, int requestId)
{
    var hasPermission = await _formAuthService.UserHasPermissionAsync(
        User, formId, FormPermissionType.DeleteRequest);
        
    if (!hasPermission)
    {
        return Forbid("You don't have permission to delete requests for this form.");
    }
    
    // Process the deletion...
    return Ok();
}
```

### 3. **Using Policy-Based Authorization**

#### Define Policies in Program.cs (Optional)
```csharp
// For specific forms (if you know the form IDs at startup)
options.AddPolicy("CanCreateCountryRequest", policy =>
    policy.RequireFormPermission(FormPermissionType.CreateRequest, formId: 5));

// Generic policies (form ID provided at runtime)
options.AddPolicy("CanViewFormData", policy =>
    policy.RequireFormPermission(FormPermissionType.ViewData));
```

#### Use Policies in Components
```razor
<PolicyAuthorizeView PolicyName="CanCreateCountryRequest">
    <button class="btn btn-primary">Add New Country</button>
    <NotAuthorized>
        <div class="alert alert-danger">Access denied.</div>
    </NotAuthorized>
</PolicyAuthorizeView>
```

### 4. **Dynamic Permission Checking**

#### Get All User Permissions for a Form
```csharp
var userPermissions = await _formAuthService.GetUserPermissionsAsync(User, formId);

if (userPermissions.Contains(FormPermissionType.ViewData))
{
    // Show data view button
}

if (userPermissions.Any(p => new[] { 
    FormPermissionType.BulkUploadCsv, 
    FormPermissionType.BulkEditRecords 
}.Contains(p)))
{
    // Show bulk operations menu
}
```

#### Check Multiple Permissions
```csharp
// User needs ANY of these permissions
var canManageData = await _formAuthService.UserHasAnyPermissionAsync(User, formId,
    FormPermissionType.CreateRequest,
    FormPermissionType.UpdateRequest,
    FormPermissionType.DeleteRequest);

// User needs ALL of these permissions
var canPerformBulkOperations = await _formAuthService.UserHasAllPermissionsAsync(User, formId,
    FormPermissionType.ViewData,
    FormPermissionType.BulkActions,
    FormPermissionType.BulkUploadCsv);
```

## Permission Types Available

| Permission | Description |
|------------|-------------|
| `CreateRequest` | Can create new requests |
| `UpdateRequest` | Can update existing requests |
| `DeleteRequest` | Can delete requests |
| `ViewData` | Can view the data in the form's table |
| `ViewDataDetails` | Can view detailed data (additional fields) |
| `BulkActions` | Can perform bulk operations |
| `BulkUploadCsv` | Can upload CSV files for bulk import |
| `BulkEditRecords` | Can edit multiple records at once |
| `BulkDeleteRecords` | Can delete multiple records at once |
| `ViewAuditLog` | Can view audit logs for the form |
| `ExportData` | Can export data from the form |
| `ManageFormSettings` | Can modify form configuration |

## Role Management

### Dynamic Role Creation
Admins can create arbitrary role names in the form builder:
1. Go to Form Builder → Permissions tab
2. Click the "+" button next to Roles
3. Type any role name (e.g., "CountryDataEditor", "RegionalAdmin", "ReadOnlyUser")
4. Assign specific permissions to that role

### Entra ID Integration
The system automatically maps user roles from Entra ID claims:
- Standard `role` claims
- Entra ID `roles` claims  
- Multiple role formats supported

## Best Practices

### 1. **Use Descriptive Role Names**
```
✅ Good: "CountryDataEditor", "FinanceApprover", "RegionalAdmin"
❌ Bad: "Role1", "User", "Admin2"
```

### 2. **Apply Principle of Least Privilege**
```csharp
// Only grant necessary permissions
var rolePermissions = new Dictionary<FormPermissionType, bool>
{
    [FormPermissionType.CreateRequest] = true,
    [FormPermissionType.UpdateRequest] = true,
    [FormPermissionType.ViewData] = true,
    [FormPermissionType.DeleteRequest] = false, // Not granted
    [FormPermissionType.BulkDeleteRecords] = false // Not granted
};
```

### 3. **Use Authorization Components**
```razor
@* Prefer this *@
<DynamicAuthorizeView FormDefinitionId="@formId" Permission="FormPermissionType.CreateRequest">
    <CreateButton />
</DynamicAuthorizeView>

@* Over manual checks everywhere *@
@if (_canCreate) { <CreateButton /> }
```

### 4. **Handle Authorization Failures Gracefully**
```razor
<DynamicAuthorizeView FormDefinitionId="@formId" Permission="FormPermissionType.ExportData">
    <button class="btn btn-success">Export</button>
    <NotAuthorized>
        <small class="text-muted">Contact your administrator for export permissions.</small>
    </NotAuthorized>
</DynamicAuthorizeView>
```

## Security Notes

- **All permission checks are server-side** - UI components only hide/show content
- **API endpoints must be protected** with attributes or manual checks
- **Permissions are cached** for performance but refreshed when changed
- **Role names are case-sensitive** and must match exactly between Entra ID and the application
- **Default policies** (Admin, FormAdmin) are automatically created for new forms

This security system provides **enterprise-grade, granular access control** while remaining **easy to use and maintain**.
