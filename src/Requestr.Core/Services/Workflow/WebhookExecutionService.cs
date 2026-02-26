using System.Net.Http.Headers;
using System.Text;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Utilities;

namespace Requestr.Core.Services.Workflow;

/// <summary>
/// Executes HTTP webhook calls as part of a workflow step.
/// Supports optional Azure Managed Identity authentication (System-Assigned or User-Assigned).
/// </summary>
public class WebhookExecutionService : IWebhookExecutionService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookExecutionService> _logger;
    private const int TimeoutSeconds = 30;
    private const string HttpClientName = "Webhook";

    // SSRF-prevention: blocked host patterns
    private static readonly string[] BlockedHosts = 
    [
        "localhost", "127.0.0.1", "::1", "0.0.0.0",
        "169.254.169.254", // Azure IMDS / cloud metadata
        "metadata.google.internal"
    ];

    private static readonly string[] BlockedCidrs = ["10.", "172.16.", "172.17.", "172.18.", "172.19.",
        "172.20.", "172.21.", "172.22.", "172.23.", "172.24.", "172.25.", "172.26.", "172.27.",
        "172.28.", "172.29.", "172.30.", "172.31.", "192.168."];

    public WebhookExecutionService(
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookExecutionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WebhookResult> ExecuteAsync(
        WebhookStepConfiguration config,
        FormRequest request,
        FormDefinition? formDefinition = null)
    {
        try
        {
            // Build system variables for template substitution
            var systemVars = WebhookTemplateEngine.BuildSystemVariables(request, formDefinition);
            var fieldValues = request.FieldValues ?? new Dictionary<string, object?>();

            // Render URL with placeholders
            var renderedUrl = WebhookTemplateEngine.Render(config.Url, fieldValues, systemVars);

            // Validate URL (SSRF prevention)
            if (!ValidateUrl(renderedUrl, out var validationError))
            {
                _logger.LogWarning("Webhook URL validation failed: {Error}. URL: {Url}", validationError, renderedUrl);
                return new WebhookResult
                {
                    Success = false,
                    StatusCode = 0,
                    ErrorMessage = $"URL validation failed: {validationError}"
                };
            }

            // Build HTTP request
            var httpRequest = BuildHttpRequest(config, renderedUrl, fieldValues, systemVars);

            // Acquire auth token if using Managed Identity
            if (config.AuthType == WebhookAuthType.ManagedIdentity)
            {
                await AttachManagedIdentityTokenAsync(httpRequest, config);
            }

            // Execute the request
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);

            _logger.LogInformation("Executing webhook: {Method} {Url}", config.HttpMethod, renderedUrl);

            var response = await client.SendAsync(httpRequest);
            var responseBody = await response.Content.ReadAsStringAsync();

            var statusCode = (int)response.StatusCode;
            var isError = statusCode >= 400;

            if (isError)
            {
                _logger.LogWarning("Webhook returned error status {StatusCode} for {Url}. Response: {Response}",
                    statusCode, renderedUrl, Truncate(responseBody, 500));
            }
            else
            {
                _logger.LogInformation("Webhook completed successfully: {StatusCode} for {Url}", statusCode, renderedUrl);
            }

            return new WebhookResult
            {
                Success = !isError || !config.FailOnError,
                StatusCode = statusCode,
                ResponseBody = Truncate(responseBody, 2000),
                ErrorMessage = isError ? $"HTTP {statusCode}: {Truncate(responseBody, 500)}" : null
            };
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Webhook timed out after {Timeout}s for URL: {Url}", TimeoutSeconds, config.Url);
            return new WebhookResult
            {
                Success = !config.FailOnError,
                StatusCode = 0,
                ErrorMessage = $"Request timed out after {TimeoutSeconds} seconds"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook execution failed for URL: {Url}", config.Url);
            return new WebhookResult
            {
                Success = !config.FailOnError,
                StatusCode = 0,
                ErrorMessage = $"Request failed: {ex.Message}"
            };
        }
    }

    private static HttpRequestMessage BuildHttpRequest(
        WebhookStepConfiguration config,
        string renderedUrl,
        Dictionary<string, object?> fieldValues,
        Dictionary<string, string> systemVars)
    {
        var method = config.HttpMethod.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            _ => HttpMethod.Post
        };

        var httpRequest = new HttpRequestMessage(method, renderedUrl);

        // Add custom headers (with variable substitution)
        foreach (var header in config.Headers)
        {
            var renderedValue = WebhookTemplateEngine.Render(header.Value, fieldValues, systemVars);
            httpRequest.Headers.TryAddWithoutValidation(header.Key, renderedValue);
        }

        // Add body if applicable and template is provided
        if (!string.IsNullOrEmpty(config.BodyTemplate) && method != HttpMethod.Get)
        {
            var renderedBody = WebhookTemplateEngine.Render(config.BodyTemplate, fieldValues, systemVars);
            httpRequest.Content = new StringContent(renderedBody, Encoding.UTF8, "application/json");
        }

        return httpRequest;
    }

    private async Task AttachManagedIdentityTokenAsync(HttpRequestMessage httpRequest, WebhookStepConfiguration config)
    {
        if (string.IsNullOrEmpty(config.ManagedIdentityScope))
        {
            _logger.LogWarning("Managed Identity auth configured but no scope provided — skipping token acquisition");
            return;
        }

        try
        {
            // Use User-Assigned MI if ClientId is set, otherwise System-Assigned
            var credential = !string.IsNullOrEmpty(config.ManagedIdentityClientId)
                ? new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = config.ManagedIdentityClientId
                })
                : new DefaultAzureCredential();

            var tokenResult = await credential.GetTokenAsync(
                new Azure.Core.TokenRequestContext(new[] { config.ManagedIdentityScope }));

            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);

            _logger.LogDebug("Acquired Managed Identity token for scope {Scope} (ClientId: {ClientId})",
                config.ManagedIdentityScope,
                string.IsNullOrEmpty(config.ManagedIdentityClientId) ? "System-Assigned" : config.ManagedIdentityClientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Managed Identity token for scope {Scope}", config.ManagedIdentityScope);
            throw new InvalidOperationException($"Failed to acquire Managed Identity token: {ex.Message}", ex);
        }
    }

    private static bool ValidateUrl(string url, out string? error)
    {
        error = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "Invalid URL format";
            return false;
        }

        if (uri.Scheme != "https" && uri.Scheme != "http")
        {
            error = "Only HTTP and HTTPS schemes are allowed";
            return false;
        }

        var host = uri.Host.ToLowerInvariant();

        foreach (var blocked in BlockedHosts)
        {
            if (host == blocked)
            {
                error = $"Host '{host}' is not allowed";
                return false;
            }
        }

        foreach (var cidr in BlockedCidrs)
        {
            if (host.StartsWith(cidr))
            {
                error = $"Private network addresses are not allowed";
                return false;
            }
        }

        return true;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;
        return value[..maxLength] + "...";
    }
}
