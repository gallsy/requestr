using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using System.Data;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Requestr.Core.Services;

public class EmailConfigurationService : IEmailConfigurationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailConfigurationService> _logger;
    private readonly string _connectionString;

    public EmailConfigurationService(IConfiguration configuration, ILogger<EmailConfigurationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found");
    }

    public async Task<Result<EmailConfiguration?>> GetConfigurationAsync()
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            // Check if Mode column exists
            const string checkColumnSql = @"
                SELECT COUNT(*) 
                FROM sys.columns 
                WHERE object_id = OBJECT_ID(N'[dbo].[EmailConfiguration]') 
                AND name = 'Mode'";
            
            var modeColumnExists = await connection.QuerySingleAsync<int>(checkColumnSql) > 0;
            
            string sql;
            if (modeColumnExists)
            {
                sql = @"
                    SELECT Id, Provider, Mode, IsEnabled, FromEmail, FromName, 
                           SmtpHost, SmtpPort, SmtpUseSsl, SmtpUsername, SmtpPassword,
                           SendGridApiKey, CreatedAt, UpdatedAt
                    FROM EmailConfiguration 
                    WHERE Id = 1";
            }
            else
            {
                sql = @"
                    SELECT Id, Provider, 0 as Mode, IsEnabled, FromEmail, FromName, 
                           SmtpHost, SmtpPort, SmtpUseSsl, SmtpUsername, SmtpPassword,
                           SendGridApiKey, CreatedAt, UpdatedAt
                    FROM EmailConfiguration 
                    WHERE Id = 1";
            }
            
            var config = await connection.QueryFirstOrDefaultAsync<EmailConfiguration>(sql);
            return Result<EmailConfiguration?>.Success(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving email configuration");
            return Result<EmailConfiguration?>.Failure("Failed to retrieve email configuration");
        }
    }

    public async Task<Result> SaveConfigurationAsync(EmailConfiguration configuration)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            configuration.UpdatedAt = DateTime.UtcNow;
            
            const string sql = @"
                MERGE EmailConfiguration AS target
                USING (SELECT 1 AS Id) AS source ON target.Id = source.Id
                WHEN MATCHED THEN
                    UPDATE SET Provider = @Provider, Mode = @Mode, IsEnabled = @IsEnabled, FromEmail = @FromEmail, 
                              FromName = @FromName, SmtpHost = @SmtpHost, SmtpPort = @SmtpPort, 
                              SmtpUseSsl = @SmtpUseSsl, SmtpUsername = @SmtpUsername, 
                              SmtpPassword = @SmtpPassword, SendGridApiKey = @SendGridApiKey, 
                              UpdatedAt = @UpdatedAt
                WHEN NOT MATCHED THEN
                    INSERT (Provider, Mode, IsEnabled, FromEmail, FromName, SmtpHost, SmtpPort, 
                           SmtpUseSsl, SmtpUsername, SmtpPassword, SendGridApiKey, CreatedAt, UpdatedAt)
                    VALUES (@Provider, @Mode, @IsEnabled, @FromEmail, @FromName, @SmtpHost, @SmtpPort, 
                           @SmtpUseSsl, @SmtpUsername, @SmtpPassword, @SendGridApiKey, @UpdatedAt, @UpdatedAt);";
            
            await connection.ExecuteAsync(sql, configuration);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving email configuration");
            return Result.Failure("Failed to save email configuration");
        }
    }

    public async Task<Result> TestConnectionAsync(EmailConfiguration configuration)
    {
        try
        {
            if (configuration.Provider == EmailProvider.SMTP)
            {
                return await TestSmtpConnectionAsync(configuration);
            }
            else if (configuration.Provider == EmailProvider.SendGrid)
            {
                return await TestSendGridConnectionAsync(configuration);
            }
            
            return Result.Failure("Unknown email provider");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing email connection");
            return Result.Failure($"Connection test failed: {ex.Message}");
        }
    }

    public async Task<Result> SendTestEmailAsync(string toEmail, string subject, string body)
    {
        try
        {
            var configResult = await GetConfigurationAsync();
            if (!configResult.IsSuccess || configResult.Value == null)
            {
                _logger.LogWarning("Email configuration not found - cannot send email");
                return Result.Failure("Email configuration not found");
            }

            var config = configResult.Value;
            if (!config.IsEnabled)
            {
                _logger.LogWarning("Email notifications are disabled - email not sent");
                return Result.Failure("Email notifications are disabled");
            }

            // Always log email details for testing/debugging regardless of configuration
            _logger.LogInformation("=== EMAIL NOTIFICATION ===");
            _logger.LogInformation("Mode: {Mode}", config.Mode);
            _logger.LogInformation("To: {ToEmail}", toEmail);
            _logger.LogInformation("Subject: {Subject}", subject);
            _logger.LogInformation("Body: {Body}", body);

            // Check if we're in test mode
            if (config.Mode == EmailMode.Test)
            {
                _logger.LogInformation("EMAIL TEST MODE: Email content logged above but not actually sent.");
                _logger.LogInformation("=== END EMAIL TEST MODE ===");
                return Result.Success();
            }

            _logger.LogInformation("Attempting to send email using {Provider} provider", config.Provider);

            if (config.Provider == EmailProvider.SMTP)
            {
                var result = await SendSmtpEmailAsync(config, toEmail, subject, body);
                _logger.LogInformation("=== EMAIL SENT (Production Mode) ===");
                return result;
            }
            else if (config.Provider == EmailProvider.SendGrid)
            {
                var result = await SendSendGridEmailAsync(config, toEmail, subject, body);
                _logger.LogInformation("=== EMAIL SENT (Production Mode) ===");
                return result;
            }

            return Result.Failure("Unknown email provider");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending test email");
            return Result.Failure($"Failed to send test email: {ex.Message}");
        }
    }

    private async Task<Result> TestSmtpConnectionAsync(EmailConfiguration config)
    {
        try
        {
            using var client = new SmtpClient(config.SmtpHost, config.SmtpPort);
            client.EnableSsl = config.SmtpUseSsl;
            
            if (!string.IsNullOrEmpty(config.SmtpUsername) && !string.IsNullOrEmpty(config.SmtpPassword))
            {
                client.Credentials = new NetworkCredential(config.SmtpUsername, config.SmtpPassword);
            }

            // Just test the connection without sending an email
            client.Timeout = 10000; // 10 seconds
            await Task.Run(() => client.Send(new MailMessage()));
            
            return Result.Success();
        }
        catch (SmtpException ex)
        {
            if (ex.StatusCode == SmtpStatusCode.Ok)
            {
                return Result.Success();
            }
            return Result.Failure($"SMTP connection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Result.Failure($"SMTP connection failed: {ex.Message}");
        }
    }

    private async Task<Result> TestSendGridConnectionAsync(EmailConfiguration config)
    {
        try
        {
            // For SendGrid, we can test by making a simple API call
            // This is a simplified test - in production, you'd use the SendGrid SDK
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.SendGridApiKey}");
            
            var response = await client.GetAsync("https://api.sendgrid.com/v3/user/account");
            
            if (response.IsSuccessStatusCode)
            {
                return Result.Success();
            }
            
            return Result.Failure($"SendGrid API test failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return Result.Failure($"SendGrid connection failed: {ex.Message}");
        }
    }

    private async Task<Result> SendSmtpEmailAsync(EmailConfiguration config, string toEmail, string subject, string body)
    {
        try
        {
            using var client = new SmtpClient(config.SmtpHost, config.SmtpPort);
            client.EnableSsl = config.SmtpUseSsl;
            
            if (!string.IsNullOrEmpty(config.SmtpUsername) && !string.IsNullOrEmpty(config.SmtpPassword))
            {
                client.Credentials = new NetworkCredential(config.SmtpUsername, config.SmtpPassword);
            }

            var message = new MailMessage
            {
                From = new MailAddress(config.FromEmail!, config.FromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            
            message.To.Add(toEmail);
            
            await client.SendMailAsync(message);
            _logger.LogInformation("SMTP email sent successfully to {ToEmail}", toEmail);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMTP email to {ToEmail}", toEmail);
            return Result.Failure($"Failed to send SMTP email: {ex.Message}");
        }
    }

    private async Task<Result> SendSendGridEmailAsync(EmailConfiguration config, string toEmail, string subject, string body)
    {
        try
        {
            // This is a simplified implementation - in production, use the SendGrid SDK
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.SendGridApiKey}");
            
            var emailData = new
            {
                personalizations = new[]
                {
                    new { to = new[] { new { email = toEmail } } }
                },
                from = new { email = config.FromEmail, name = config.FromName },
                subject = subject,
                content = new[]
                {
                    new { type = "text/html", value = body }
                }
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(emailData);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            var response = await client.PostAsync("https://api.sendgrid.com/v3/mail/send", content);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("SendGrid email sent successfully to {ToEmail}", toEmail);
                return Result.Success();
            }
            
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("SendGrid API error: {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
            return Result.Failure($"SendGrid API error: {response.StatusCode} - {errorContent}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SendGrid email to {ToEmail}", toEmail);
            return Result.Failure($"Failed to send SendGrid email: {ex.Message}");
        }
    }
}

public class NotificationTemplateService : INotificationTemplateService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationTemplateService> _logger;
    private readonly string _connectionString;

    public NotificationTemplateService(IConfiguration configuration, ILogger<NotificationTemplateService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found");
    }

    public async Task<Result<List<NotificationTemplate>>> GetAllTemplatesAsync()
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            const string sql = "SELECT * FROM NotificationTemplates ORDER BY Name";
            
            var templates = await connection.QueryAsync<NotificationTemplate>(sql);
            return Result<List<NotificationTemplate>>.Success(templates.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving notification templates");
            return Result<List<NotificationTemplate>>.Failure("Failed to retrieve notification templates");
        }
    }

    public async Task<Result<NotificationTemplate?>> GetTemplateAsync(int id)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            const string sql = "SELECT * FROM NotificationTemplates WHERE Id = @Id";
            
            var template = await connection.QueryFirstOrDefaultAsync<NotificationTemplate>(sql, new { Id = id });
            return Result<NotificationTemplate?>.Success(template);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving notification template {Id}", id);
            return Result<NotificationTemplate?>.Failure("Failed to retrieve notification template");
        }
    }

    public async Task<Result<NotificationTemplate?>> GetTemplateByKeyAsync(string templateKey)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            const string sql = "SELECT * FROM NotificationTemplates WHERE TemplateKey = @TemplateKey";
            
            var template = await connection.QueryFirstOrDefaultAsync<NotificationTemplate>(sql, new { TemplateKey = templateKey });
            return Result<NotificationTemplate?>.Success(template);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving notification template by key {TemplateKey}", templateKey);
            return Result<NotificationTemplate?>.Failure("Failed to retrieve notification template");
        }
    }

    public async Task<Result<NotificationTemplate>> SaveTemplateAsync(NotificationTemplate template)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            if (template.Id == 0)
            {
                // Insert new template
                template.CreatedAt = DateTime.UtcNow;
                template.UpdatedAt = DateTime.UtcNow;
                
                const string insertSql = @"
                    INSERT INTO NotificationTemplates (Name, TemplateKey, Subject, Body, IsEnabled, CreatedAt, UpdatedAt)
                    OUTPUT INSERTED.Id
                    VALUES (@Name, @TemplateKey, @Subject, @Body, @IsEnabled, @CreatedAt, @UpdatedAt)";
                
                template.Id = await connection.QuerySingleAsync<int>(insertSql, template);
            }
            else
            {
                // Update existing template
                template.UpdatedAt = DateTime.UtcNow;
                
                const string updateSql = @"
                    UPDATE NotificationTemplates 
                    SET Name = @Name, TemplateKey = @TemplateKey, Subject = @Subject, 
                        Body = @Body, IsEnabled = @IsEnabled, UpdatedAt = @UpdatedAt
                    WHERE Id = @Id";
                
                await connection.ExecuteAsync(updateSql, template);
            }
            
            return Result<NotificationTemplate>.Success(template);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving notification template");
            return Result<NotificationTemplate>.Failure("Failed to save notification template");
        }
    }

    public async Task<Result> DeleteTemplateAsync(int id)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            const string sql = "DELETE FROM NotificationTemplates WHERE Id = @Id";
            
            await connection.ExecuteAsync(sql, new { Id = id });
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting notification template {Id}", id);
            return Result.Failure("Failed to delete notification template");
        }
    }

    public Task<Result<List<NotificationTemplate>>> GetDefaultTemplatesAsync()
    {
        var defaultTemplates = new List<NotificationTemplate>
        {
            new()
            {
                Name = "New Request Created",
                TemplateKey = NotificationTemplateKeys.NewRequestCreated,
                Subject = "New Request Submitted: {{FormName}} - {{RequestId}}",
                Body = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>New Request Created</title>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 20px; background-color: #f8f9fa; }
        .container { max-width: 600px; margin: 0 auto; background: white; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); overflow: hidden; }
        .header { background: linear-gradient(135deg, #0d6efd, #0056b3); color: white; padding: 30px 20px; text-align: center; }
        .header h1 { margin: 0; font-size: 24px; font-weight: 600; }
        .content { padding: 30px 20px; }
        .info-grid { display: grid; gap: 15px; margin: 20px 0; }
        .info-item { background: #f8f9fa; padding: 15px; border-radius: 6px; border-left: 4px solid #0d6efd; }
        .info-label { font-weight: 600; color: #495057; font-size: 14px; margin-bottom: 5px; }
        .info-value { color: #212529; font-size: 16px; }
        .action-button { display: inline-block; background: #0d6efd; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: 500; margin: 20px 0; }
        .footer { background: #f8f9fa; padding: 20px; text-align: center; color: #6c757d; font-size: 14px; border-top: 1px solid #dee2e6; }
        .status-badge { display: inline-block; background: #28a745; color: white; padding: 4px 12px; border-radius: 12px; font-size: 12px; font-weight: 500; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🆕 New Request Submitted</h1>
            <p style='margin: 10px 0 0 0; opacity: 0.9;'>A new request requires your attention</p>
        </div>
        <div class='content'>
            <div class='info-grid'>
                <div class='info-item'>
                    <div class='info-label'>📋 Form Name</div>
                    <div class='info-value'>{{FormName}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>🔢 Request ID</div>
                    <div class='info-value'>{{RequestId}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>📝 Description</div>
                    <div class='info-value'>{{RequestDescription}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>👤 Requested by</div>
                    <div class='info-value'>{{CreatingUser}} ({{CreatingUserEmail}})</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>💬 Comments</div>
                    <div class='info-value'>{{RequestComments}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>📅 Created Date</div>
                    <div class='info-value'>{{RequestCreatedDate}}</div>
                </div>
            </div>
            <div style='text-align: center;'>
                <a href='{{RequestUrl}}' class='action-button'>🔍 View Request Details</a>
            </div>
        </div>
        <div class='footer'>
            <p>This is an automated notification from the Requestr system.<br>
            Please do not reply to this email.</p>
        </div>
    </div>
</body>
</html>",
                IsEnabled = true
            },
            new()
            {
                Name = "Request Approved",
                TemplateKey = NotificationTemplateKeys.RequestApproved,
                Subject = "✅ Request Approved: {{FormName}} - {{RequestId}}",
                Body = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Request Approved</title>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 20px; background-color: #f8f9fa; }
        .container { max-width: 600px; margin: 0 auto; background: white; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); overflow: hidden; }
        .header { background: linear-gradient(135deg, #28a745, #1e7e34); color: white; padding: 30px 20px; text-align: center; }
        .header h1 { margin: 0; font-size: 24px; font-weight: 600; }
        .content { padding: 30px 20px; }
        .success-banner { background: #d4edda; border: 1px solid #c3e6cb; color: #155724; padding: 15px; border-radius: 6px; margin-bottom: 20px; text-align: center; font-weight: 500; }
        .info-grid { display: grid; gap: 15px; margin: 20px 0; }
        .info-item { background: #f8f9fa; padding: 15px; border-radius: 6px; border-left: 4px solid #28a745; }
        .info-label { font-weight: 600; color: #495057; font-size: 14px; margin-bottom: 5px; }
        .info-value { color: #212529; font-size: 16px; }
        .action-button { display: inline-block; background: #28a745; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: 500; margin: 20px 0; }
        .footer { background: #f8f9fa; padding: 20px; text-align: center; color: #6c757d; font-size: 14px; border-top: 1px solid #dee2e6; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>✅ Request Approved</h1>
            <p style='margin: 10px 0 0 0; opacity: 0.9;'>Your request has been approved</p>
        </div>
        <div class='content'>
            <div class='success-banner'>
                🎉 Great news! Your request has been approved and will be processed.
            </div>
            <div class='info-grid'>
                <div class='info-item'>
                    <div class='info-label'>📋 Form Name</div>
                    <div class='info-value'>{{FormName}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>🔢 Request ID</div>
                    <div class='info-value'>{{RequestId}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>📝 Description</div>
                    <div class='info-value'>{{RequestDescription}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>👨‍💼 Approved by</div>
                    <div class='info-value'>{{ApproverName}} ({{ApproverEmail}})</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>📅 Approval Date</div>
                    <div class='info-value'>{{ApprovalDate}}</div>
                </div>
            </div>
            <div style='text-align: center;'>
                <a href='{{RequestUrl}}' class='action-button'>📋 View Request Details</a>
            </div>
            <div style='background: #e7f3ff; border: 1px solid #b8daff; color: #004085; padding: 15px; border-radius: 6px; margin-top: 20px;'>
                <strong>Next Steps:</strong> Your approved request will now be processed according to the defined workflow. You will receive updates as the request progresses.
            </div>
        </div>
        <div class='footer'>
            <p>This is an automated notification from the Requestr system.<br>
            Please do not reply to this email.</p>
        </div>
    </div>
</body>
</html>",
                IsEnabled = true
            },
            new()
            {
                Name = "Request Rejected",
                TemplateKey = NotificationTemplateKeys.RequestRejected,
                Subject = "❌ Request Rejected: {{FormName}} - {{RequestId}}",
                Body = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Request Rejected</title>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 20px; background-color: #f8f9fa; }
        .container { max-width: 600px; margin: 0 auto; background: white; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); overflow: hidden; }
        .header { background: linear-gradient(135deg, #dc3545, #c82333); color: white; padding: 30px 20px; text-align: center; }
        .header h1 { margin: 0; font-size: 24px; font-weight: 600; }
        .content { padding: 30px 20px; }
        .rejection-banner { background: #f8d7da; border: 1px solid #f5c6cb; color: #721c24; padding: 15px; border-radius: 6px; margin-bottom: 20px; text-align: center; font-weight: 500; }
        .info-grid { display: grid; gap: 15px; margin: 20px 0; }
        .info-item { background: #f8f9fa; padding: 15px; border-radius: 6px; border-left: 4px solid #dc3545; }
        .info-label { font-weight: 600; color: #495057; font-size: 14px; margin-bottom: 5px; }
        .info-value { color: #212529; font-size: 16px; }
        .action-button { display: inline-block; background: #dc3545; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: 500; margin: 20px 0; }
        .footer { background: #f8f9fa; padding: 20px; text-align: center; color: #6c757d; font-size: 14px; border-top: 1px solid #dee2e6; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>❌ Request Rejected</h1>
            <p style='margin: 10px 0 0 0; opacity: 0.9;'>Your request has been rejected</p>
        </div>
        <div class='content'>
            <div class='rejection-banner'>
                Unfortunately, your request has been rejected. Please review the details below.
            </div>
            <div class='info-grid'>
                <div class='info-item'>
                    <div class='info-label'>📋 Form Name</div>
                    <div class='info-value'>{{FormName}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>🔢 Request ID</div>
                    <div class='info-value'>{{RequestId}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>📝 Description</div>
                    <div class='info-value'>{{RequestDescription}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>👨‍💼 Rejected by</div>
                    <div class='info-value'>{{ApproverName}} ({{ApproverEmail}})</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>💬 Rejection Reason</div>
                    <div class='info-value'>{{RejectionReason}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>📅 Rejection Date</div>
                    <div class='info-value'>{{RejectionDate}}</div>
                </div>
            </div>
            <div style='text-align: center;'>
                <a href='{{RequestUrl}}' class='action-button'>📋 View Request Details</a>
            </div>
            <div style='background: #fff3cd; border: 1px solid #ffeaa7; color: #856404; padding: 15px; border-radius: 6px; margin-top: 20px;'>
                <strong>Next Steps:</strong> You may review the rejection reason and consider submitting a revised request if appropriate. If you have questions about this decision, please contact your administrator.
            </div>
        </div>
        <div class='footer'>
            <p>This is an automated notification from the Requestr system.<br>
            Please do not reply to this email.</p>
        </div>
    </div>
</body>
</html>",
                IsEnabled = true
            },
            new()
            {
                Name = "Workflow Step Pending",
                TemplateKey = NotificationTemplateKeys.WorkflowStepPending,
                Subject = "⏳ Action Required: {{WorkflowStepName}} - {{RequestId}}",
                Body = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Action Required</title>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 20px; background-color: #f8f9fa; }
        .container { max-width: 600px; margin: 0 auto; background: white; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); overflow: hidden; }
        .header { background: linear-gradient(135deg, #fd7e14, #e55a00); color: white; padding: 30px 20px; text-align: center; }
        .header h1 { margin: 0; font-size: 24px; font-weight: 600; }
        .content { padding: 30px 20px; }
        .urgent-banner { background: #fff3cd; border: 1px solid #ffeaa7; color: #856404; padding: 15px; border-radius: 6px; margin-bottom: 20px; text-align: center; font-weight: 500; }
        .info-grid { display: grid; gap: 15px; margin: 20px 0; }
        .info-item { background: #f8f9fa; padding: 15px; border-radius: 6px; border-left: 4px solid #fd7e14; }
        .info-label { font-weight: 600; color: #495057; font-size: 14px; margin-bottom: 5px; }
        .info-value { color: #212529; font-size: 16px; }
        .action-button { display: inline-block; background: #fd7e14; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: 500; margin: 20px 0; }
        .footer { background: #f8f9fa; padding: 20px; text-align: center; color: #6c757d; font-size: 14px; border-top: 1px solid #dee2e6; }
        .priority-badge { display: inline-block; background: #fd7e14; color: white; padding: 4px 12px; border-radius: 12px; font-size: 12px; font-weight: 500; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>⏳ Action Required</h1>
            <p style='margin: 10px 0 0 0; opacity: 0.9;'>A workflow step needs your attention</p>
        </div>
        <div class='content'>
            <div class='urgent-banner'>
                ⚡ This workflow step requires your immediate attention to keep the request moving forward.
            </div>
            <div class='info-grid'>
                <div class='info-item'>
                    <div class='info-label'>🔄 Workflow Name</div>
                    <div class='info-value'>{{WorkflowName}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>📍 Current Step</div>
                    <div class='info-value'>{{WorkflowStepName}} <span class='priority-badge'>PENDING</span></div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>🔢 Request ID</div>
                    <div class='info-value'>{{RequestId}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>📋 Form Name</div>
                    <div class='info-value'>{{FormName}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>📝 Description</div>
                    <div class='info-value'>{{RequestDescription}}</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>👤 Assigned To</div>
                    <div class='info-value'>{{AssignedUserName}} ({{AssignedUserEmail}})</div>
                </div>
                <div class='info-item'>
                    <div class='info-label'>📅 Due Date</div>
                    <div class='info-value'>{{DueDate}}</div>
                </div>
            </div>
            <div style='text-align: center;'>
                <a href='{{RequestUrl}}' class='action-button'>🚀 Take Action Now</a>
            </div>
            <div style='background: #e7f3ff; border: 1px solid #b8daff; color: #004085; padding: 15px; border-radius: 6px; margin-top: 20px;'>
                <strong>What's Next:</strong> Please review the request details and take the appropriate action to move this workflow step forward. Other team members may be waiting on your decision.
            </div>
        </div>
        <div class='footer'>
            <p>This is an automated notification from the Requestr system.<br>
            Please do not reply to this email.</p>
        </div>
    </div>
</body>
</html>",
                IsEnabled = true
            }
        };

        return Task.FromResult(Result<List<NotificationTemplate>>.Success(defaultTemplates));
    }

    public async Task<Result> CreateDefaultTemplatesAsync()
    {
        try
        {
            var defaultTemplatesResult = await GetDefaultTemplatesAsync();
            if (!defaultTemplatesResult.IsSuccess)
            {
                return Result.Failure("Failed to get default templates");
            }

            foreach (var template in defaultTemplatesResult.Value!)
            {
                var existingResult = await GetTemplateByKeyAsync(template.TemplateKey);
                if (existingResult.IsSuccess && existingResult.Value == null)
                {
                    await SaveTemplateAsync(template);
                }
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating default templates");
            return Result.Failure("Failed to create default templates");
        }
    }
}

public class AdvancedNotificationService : IAdvancedNotificationService, INotificationService
{
    private readonly IEmailConfigurationService _emailConfigurationService;
    private readonly INotificationTemplateService _templateService;
    private readonly ILogger<AdvancedNotificationService> _logger;

    public AdvancedNotificationService(
        IEmailConfigurationService emailConfigurationService,
        INotificationTemplateService templateService,
        ILogger<AdvancedNotificationService> logger)
    {
        _emailConfigurationService = emailConfigurationService;
        _templateService = templateService;
        _logger = logger;
    }

    public async Task<Result> SendNotificationAsync(string templateKey, Dictionary<string, string> variables, string toEmail, string? toName = null)
    {
        try
        {
            var templateResult = await _templateService.GetTemplateByKeyAsync(templateKey);
            if (!templateResult.IsSuccess || templateResult.Value == null)
            {
                _logger.LogWarning("Template not found: {TemplateKey}", templateKey);
                return Result.Failure($"Template not found: {templateKey}");
            }

            var template = templateResult.Value;
            if (!template.IsEnabled)
            {
                _logger.LogWarning("Template is disabled: {TemplateKey}", templateKey);
                return Result.Failure($"Template is disabled: {templateKey}");
            }

            var subject = ReplaceVariables(template.Subject, variables);
            var body = ReplaceVariables(template.Body, variables);

            // Always log notification details for testing/debugging
            LogNotificationDetails(templateKey, subject, body, toEmail, toName, variables);

            return await SendCustomNotificationAsync(subject, body, toEmail, toName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification with template {TemplateKey}", templateKey);
            return Result.Failure($"Failed to send notification: {ex.Message}");
        }
    }

    public async Task<Result> SendCustomNotificationAsync(string subject, string body, string toEmail, string? toName = null)
    {
        try
        {
            // Always log custom notification details for testing/debugging
            LogNotificationDetails("CUSTOM", subject, body, toEmail, toName, new Dictionary<string, string>());

            return await _emailConfigurationService.SendTestEmailAsync(toEmail, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending custom notification");
            return Result.Failure($"Failed to send notification: {ex.Message}");
        }
    }

    public string ReplaceVariables(string template, Dictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        var result = template;
        foreach (var variable in variables)
        {
            result = result.Replace(variable.Key, variable.Value ?? string.Empty);
        }

        return result;
    }

    private void LogNotificationDetails(string templateKey, string subject, string body, string toEmail, string? toName, Dictionary<string, string> variables)
    {
        _logger.LogInformation("=== NOTIFICATION DETAILS (Testing) ===");
        _logger.LogInformation("Template: {TemplateKey}", templateKey);
        _logger.LogInformation("To: {ToEmail} {ToName}", toEmail, toName ?? "");
        _logger.LogInformation("Subject: {Subject}", subject);
        _logger.LogInformation("Body: {Body}", body);
        
        if (variables.Any())
        {
            _logger.LogInformation("Variables used:");
            foreach (var variable in variables)
            {
                _logger.LogInformation("  {Key} = {Value}", variable.Key, variable.Value ?? "NULL");
            }
        }
        
        _logger.LogInformation("=== END NOTIFICATION DETAILS ===");
    }
}
