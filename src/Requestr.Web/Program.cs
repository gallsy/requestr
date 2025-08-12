using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using BlazorBootstrap;
using Requestr.Core.Extensions;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Web.Authorization;
using Serilog;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .CreateLogger();

builder.Host.UseSerilog();

// Add Azure App Service logging
if (builder.Environment.IsProduction())
{
    builder.Logging.AddAzureWebAppDiagnostics();
}

// Add Application Insights (if connection string is available)
if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

// Add services to the container
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor()
    .AddMicrosoftIdentityConsentHandler();

// Add BlazorBootstrap
builder.Services.AddBlazorBootstrap();

// Add application services
builder.Services.AddRequestrCore();

// Add custom authorization services
builder.Services.AddScoped<IFormAuthorizationService, FormAuthorizationService>();

// Add toast notification service
builder.Services.AddScoped<Requestr.Web.Services.IToastNotificationService, Requestr.Web.Services.ToastNotificationService>();

// Configure authorization
builder.Services.AddAuthorization(options =>
{
    // Default policy requires authentication
    options.FallbackPolicy = options.DefaultPolicy;
    
    // Admin policy for form management
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));
    
    // Approver policy for approving requests
    options.AddPolicy("CanApprove", policy =>
        policy.RequireRole("Admin"));
});

var app = builder.Build();

// Configure forwarded headers FIRST - must be before authentication
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
    // Trust Azure App Service proxy
    KnownProxies = { },
    KnownNetworks = { }
});

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    // Only use HTTPS redirection in production
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");
app.MapRazorPages();

try
{
    Log.Information("Starting Requestr application");
    Log.Information("Environment: {Environment}", app.Environment.EnvironmentName);
    Log.Information("Application started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
