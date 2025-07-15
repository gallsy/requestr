using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

public interface IEmailConfigurationService
{
    Task<Result<EmailConfiguration?>> GetConfigurationAsync();
    Task<Result> SaveConfigurationAsync(EmailConfiguration configuration);
    Task<Result> TestConnectionAsync(EmailConfiguration configuration);
    Task<Result> SendTestEmailAsync(string toEmail, string subject, string body);
}

public interface INotificationTemplateService
{
    Task<Result<List<NotificationTemplate>>> GetAllTemplatesAsync();
    Task<Result<NotificationTemplate?>> GetTemplateAsync(int id);
    Task<Result<NotificationTemplate?>> GetTemplateByKeyAsync(string templateKey);
    Task<Result<NotificationTemplate>> SaveTemplateAsync(NotificationTemplate template);
    Task<Result> DeleteTemplateAsync(int id);
    Task<Result<List<NotificationTemplate>>> GetDefaultTemplatesAsync();
    Task<Result> CreateDefaultTemplatesAsync();
}

public interface IAdvancedNotificationService
{
    Task<Result> SendNotificationAsync(string templateKey, Dictionary<string, string> variables, string toEmail, string? toName = null);
    Task<Result> SendCustomNotificationAsync(string subject, string body, string toEmail, string? toName = null);
    string ReplaceVariables(string template, Dictionary<string, string> variables);
}
