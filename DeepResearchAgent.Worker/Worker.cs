using DeepResearchAgent.Engine.Interfaces;

namespace DeepResearchAgent.Worker;

public class Worker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IServiceProvider serviceProvider,
        ILogger<Worker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Research Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();

            var researchService =
                scope.ServiceProvider.GetRequiredService<IResearchService>();

            var processed =
                await researchService.ProcessPendingResearchAsync(
                    stoppingToken);

            if (processed)
            {
                _logger.LogInformation(
                    "Pending research processed.");
            }
            else
            {
                _logger.LogInformation(
                    "No pending research found.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(5),
                stoppingToken);
        }
    }
}