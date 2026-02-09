using Microsoft.Extensions.DependencyInjection;
using Requestr.Core.Interfaces;
using Requestr.Core.Repositories;
using Requestr.Core.Services;
using Requestr.Core.Services.FormRequests;
using Requestr.Core.Services.Workflow;

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
        
        // Workflow Repositories (Phase 3)
        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
        services.AddScoped<IWorkflowStepRepository, WorkflowStepRepository>();
        services.AddScoped<IWorkflowTransitionRepository, WorkflowTransitionRepository>();
        services.AddScoped<IWorkflowInstanceRepository, WorkflowInstanceRepository>();
        services.AddScoped<IWorkflowStepInstanceRepository, WorkflowStepInstanceRepository>();
        
        // Data Services
        services.AddScoped<IDatabaseService, DatabaseService>();
        services.AddScoped<IDataService, DataService>();
        
        // Business Services
        services.AddScoped<IFormDefinitionService, FormDefinitionService>();
        services.AddScoped<IBulkFormRequestService, BulkFormRequestService>();
        services.AddScoped<IDataViewService, DataViewService>();
        
        // FormRequest Services
        services.AddScoped<IFormRequestQueryService, FormRequestQueryService>();
        services.AddScoped<IFormRequestCommandService, FormRequestCommandService>();
        services.AddScoped<IFormRequestApprovalService, FormRequestApprovalService>();
        services.AddScoped<IFormRequestApplicationService, FormRequestApplicationService>();
        services.AddScoped<IFormRequestHistoryService, FormRequestHistoryService>();
        
        // Workflow Designer and Configuration Services
        services.AddScoped<IWorkflowDesignerService, WorkflowDesignerService>();
        services.AddScoped<IFormWorkflowConfigurationService, FormWorkflowConfigurationService>();
        services.AddScoped<IFormPermissionService, FormPermissionService>();
        
        // Decomposed Workflow Services (Phase 3)
        // CQRS-based workflow definition services
        services.AddScoped<IWorkflowDefinitionCommandService, WorkflowDefinitionCommandService>();
        services.AddScoped<IWorkflowDefinitionQueryService, WorkflowDefinitionQueryService>();
        // Workflow lifecycle and execution services
        services.AddScoped<IWorkflowInstanceService, WorkflowInstanceService>();
        services.AddScoped<IWorkflowExecutionService, WorkflowExecutionService>();
        services.AddScoped<IWorkflowProgressService, WorkflowProgressService>();
        
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
