using Requestr.Core.Services;

namespace Requestr.Web.Services;

/// <summary>Runs the record key backfill once per app start; it is a no-op once all keys are filled.</summary>
public sealed class RecordKeyBackfillHostedService(IServiceScopeFactory scopeFactory, ILogger<RecordKeyBackfillHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RecordKeyBackfillService>().RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Record key backfill failed; it will retry on next start");
        }
    }
}
