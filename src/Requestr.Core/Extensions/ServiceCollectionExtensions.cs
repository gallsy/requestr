using Microsoft.Extensions.DependencyInjection;
using Requestr.Core.Interfaces;
using Requestr.Core.Services;

namespace Requestr.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRequestrCore(this IServiceCollection services)
    {
        // Data Services
        services.AddScoped<IDatabaseService, DatabaseService>();
        services.AddScoped<IDataService, DataService>();
        
        // Business Services
        services.AddScoped<IFormDefinitionService, FormDefinitionService>();
        services.AddScoped<IFormRequestService, FormRequestService>();
        services.AddScoped<IBulkFormRequestService, BulkFormRequestService>();
        services.AddScoped<IDataViewService, DataViewService>();
        
        // Workflow Services
        services.AddScoped<IWorkflowService, WorkflowService>();
        services.AddScoped<IWorkflowDesignerService, WorkflowDesignerService>();
        services.AddScoped<IFormWorkflowConfigurationService, FormWorkflowConfigurationService>();
        services.AddScoped<IFormPermissionService, FormPermissionService>();
        
        // Notification Services
        services.AddScoped<IEmailConfigurationService, EmailConfigurationService>();
        services.AddScoped<INotificationTemplateService, NotificationTemplateService>();
        services.AddScoped<IAdvancedNotificationService, AdvancedNotificationService>();
        
        // Business Logic Services (to be implemented)
        // services.AddScoped<IValidationService, ValidationService>();
        // services.AddScoped<IAuthorizationService, AuthorizationService>();
        // services.AddScoped<INotificationService, NotificationService>();
        // services.AddScoped<IAuditService, AuditService>();
        
        // UI Services
        services.AddSingleton<ThemeService>();

        return services;
    }
}
