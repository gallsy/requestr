namespace Requestr.Core.Models;

public static class Constants
{
    public static class Roles
    {
        public const string Admin = "Admin";
        public const string FormAdmin = "FormAdmin";
        public const string DataAdmin = "DataAdmin";
        public const string ReferenceDataApprover = "ReferenceDataApprover";
        public const string User = "User";
    }

    public static class RequestTypes
    {
        public const string Insert = "INSERT";
        public const string Update = "UPDATE";
        public const string Delete = "DELETE";
    }

    public static class ValidationMessages
    {
        public const string Required = "This field is required";
        public const string InvalidFormat = "Invalid format";
        public const string MaxLengthExceeded = "Maximum length exceeded";
        public const string InvalidEmail = "Invalid email format";
        public const string InvalidUrl = "Invalid URL format";
        public const string InvalidPhone = "Invalid phone number format";
    }

    public static class AuditActions
    {
        public const string FormCreated = "FormCreated";
        public const string FormUpdated = "FormUpdated";
        public const string FormDeleted = "FormDeleted";
        public const string RequestCreated = "RequestCreated";
        public const string RequestApproved = "RequestApproved";
        public const string RequestRejected = "RequestRejected";
        public const string RequestApplied = "RequestApplied";
        public const string UserLogin = "UserLogin";
        public const string UserLogout = "UserLogout";
    }

    public static class DatabaseSchemas
    {
        public const string Default = "dbo";
        public const string Audit = "audit";
        public const string Config = "config";
    }

    public static class Limits
    {
        public const int MaxFormNameLength = 100;
        public const int MaxDescriptionLength = 500;
        public const int MaxFieldNameLength = 50;
        public const int MaxDisplayNameLength = 100;
        public const int MaxValidationMessageLength = 200;
        public const int MaxCommentsLength = 1000;
        public const int DefaultQueryLimit = 100;
        public const int MaxQueryLimit = 1000;
    }
}
