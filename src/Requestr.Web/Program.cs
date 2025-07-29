using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using BlazorBootstrap;
using Requestr.Core.Extensions;
using Requestr.Core.Models;
using Requestr.Web.Authorization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

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

// Configure authorization
builder.Services.AddAuthorization(options =>
{
    // Default policy requires authentication
    options.FallbackPolicy = options.DefaultPolicy;
    
    // Admin policy for form management
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin", "FormAdmin"));
    
    // Approver policy for approving requests
    options.AddPolicy("CanApprove", policy =>
        policy.RequireRole("Admin", "FormAdmin", "DataAdmin", "ReferenceDataApprover"));
});

var app = builder.Build();

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
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
