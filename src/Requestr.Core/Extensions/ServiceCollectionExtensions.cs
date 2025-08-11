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
        
        // Register INotificationService as the same implementation as IAdvancedNotificationService
        services.AddScoped<INotificationService, AdvancedNotificationService>();
        
        // Remove unused interfaces - functionality is implemented elsewhere:
        // - Validation: Distributed across components (FormRenderer, DTOs, etc.)
        // - Authorization: Handled by existing FormPermissionService and ASP.NET Core
        // - Audit: Comprehensive FormRequestHistory system exists
        
        // Conflict Detection Services
        services.AddScoped<IConflictDetectionService, ConflictDetectionService>();
        
        // Input Validation Services
        services.AddScoped<Services.IInputValidationService, Services.InputValidationService>();
        
        // UI Services
        services.AddSingleton<ThemeService>();

        return services;
    }
}
