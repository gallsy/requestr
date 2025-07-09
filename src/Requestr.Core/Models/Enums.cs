namespace Requestr.Core.Models;

public enum RequestStatus
{
    Pending,
    Approved,
    Rejected,
    Applied,
    Failed  // Added for when application to target database fails
}

public enum FormRequestChangeType
{
    Created,
    Updated,
    StatusChanged,
    Approved,
    Rejected,
    Applied,
    Failed  // Added for when application to target database fails
}

public enum RequestType
{
    Insert,
    Update,
    Delete
}

public enum FieldDataType
{
    String,
    Integer,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Time,
    Text,
    Email,
    Phone,
    Url
}

public enum AuthorizationResult
{
    Authorized,
    Unauthorized,
    Forbidden
}
