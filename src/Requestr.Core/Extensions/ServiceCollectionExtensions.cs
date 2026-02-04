using Microsoft.Extensions.DependencyInjection;
using Requestr.Core.Interfaces;
using Requestr.Core.Repositories;
using Requestr.Core.Services;
using Requestr.Core.Services.FormRequests;

namespace Requestr.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRequestrCore(this IServiceCollection services)
    {
        // Database Infrastructure
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
        
        // Repositories
        services.AddScoped<IFormRequestRepository, FormRequestRepository>();
        services.AddScoped<IFormRequestHistoryRepository, FormRequestHistoryRepository>();
        
        // Data Services
        services.AddScoped<IDatabaseService, DatabaseService>();
        services.AddScoped<IDataService, DataService>();
        
        // Business Services
        services.AddScoped<IFormDefinitionService, FormDefinitionService>();
        services.AddScoped<IFormRequestService, FormRequestService>();
        services.AddScoped<IBulkFormRequestService, BulkFormRequestService>();
        services.AddScoped<IDataViewService, DataViewService>();
        
        // Decomposed FormRequest Services (Phase 2)
        // These provide single-responsibility alternatives to the monolithic FormRequestService
        services.AddScoped<IFormRequestQueryService, FormRequestQueryService>();
        services.AddScoped<IFormRequestCommandService, FormRequestCommandService>();
        services.AddScoped<IFormRequestApprovalService, FormRequestApprovalService>();
        services.AddScoped<IFormRequestApplicationService, FormRequestApplicationService>();
        services.AddScoped<IFormRequestHistoryService, FormRequestHistoryService>();
        
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
        
    // User Services
    services.AddScoped<IUserService, UserService>();
        
    // UI Services (per-circuit in Blazor Server)
    services.AddScoped<ThemeService>();

        return services;
    }
}
