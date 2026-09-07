namespace Ustin.Work.LessMess.UmbarcoWrapper.Worker;

/// <summary>
/// Placeholder background service. Replace with real jobs
/// (refresh-token cleanup, outbox publisher, event consumers).
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker placeholder started; no jobs registered yet");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
