using JiraTelegramBot.Digest;
using JiraTelegramBot.Scheduling;

namespace JiraTelegramBot;

/// <summary>
/// Фоновый цикл: ждёт до следующего времени уведомления и запускает дневную сводку.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NextRunCalculator _calculator;
    private readonly TimeProvider _clock;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IServiceScopeFactory scopeFactory,
        NextRunCalculator calculator,
        TimeProvider clock,
        ILogger<Worker> logger)
    {
        _scopeFactory = scopeFactory;
        _calculator = calculator;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = _clock.GetUtcNow();
            var next = _calculator.GetNextRun(now);
            var delay = next - now;

            _logger.LogInformation(
                "Следующая сводка запланирована на {Next:yyyy-MM-dd HH:mm zzz} (через {Delay:c}).",
                next, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            using var scope = _scopeFactory.CreateScope();
            var digest = scope.ServiceProvider.GetRequiredService<DailyDigestService>();
            await digest.RunAsync(stoppingToken);
        }
    }
}
