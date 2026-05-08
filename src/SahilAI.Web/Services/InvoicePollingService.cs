using Microsoft.AspNetCore.SignalR;
using SahilAI.Domain.Interfaces;
using SahilAI.Web.Hubs;

namespace SahilAI.Web.Services;

/// <summary>
/// Background service that polls the database every 3 seconds, detects changes,
/// updates the <see cref="LiveCountService"/> singleton (which notifies Blazor
/// components via C# events), and also broadcasts to any external SignalR clients
/// connected to <see cref="InvoiceHub"/>.
/// </summary>
public sealed class InvoicePollingService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly IServiceScopeFactory          _scopeFactory;
    private readonly LiveCountService              _live;
    private readonly IHubContext<InvoiceHub>        _hub;
    private readonly ILogger<InvoicePollingService> _logger;

    // ── Diff tracking ─────────────────────────────────────────────────────────
    private Dictionary<string, int> _lastCounts    = [];
    private HashSet<int>            _knownReviewIds = [];
    private bool                    _initialized;

    public InvoicePollingService(
        IServiceScopeFactory          scopeFactory,
        LiveCountService              live,
        IHubContext<InvoiceHub>        hub,
        ILogger<InvoicePollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _live   = live;
        _hub    = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[LivePoll] Invoice polling service started — interval {Sec}s.", PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[LivePoll] Tick failed — will retry on next interval.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    private async Task PollAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();

        // ── 1. Status counts ─────────────────────────────────────────────────
        var counts = await repo.GetStatusCountsAsync();

        var countsChanged = !_initialized
            || counts.Any(kv => !_lastCounts.TryGetValue(kv.Key, out var v) || v != kv.Value)
            || _lastCounts.Any(kv => !counts.ContainsKey(kv.Key));

        if (countsChanged)
        {
            _lastCounts = new Dictionary<string, int>(counts);
            _live.UpdateCounts(counts);

            // Push to any external (non-Blazor) SignalR clients
            await _hub.Clients.Group("dashboard").SendAsync("countsUpdated", counts);
        }

        // ── 2. Review queue diff ─────────────────────────────────────────────
        var reviewQueue = (await repo.GetReviewQueueAsync()).ToList();
        var currentIds  = reviewQueue.Select(i => i.Id).ToHashSet();

        var newArrivals = _initialized
            ? reviewQueue.Where(i => !_knownReviewIds.Contains(i.Id)).ToList()
            : (IEnumerable<Domain.Entities.Invoice>)[];

        if (!_initialized || !currentIds.SetEquals(_knownReviewIds))
        {
            _knownReviewIds = currentIds;
            _live.UpdateReviewQueue(reviewQueue, newArrivals);

            foreach (var inv in newArrivals)
            {
                _logger.LogInformation(
                    "[LivePoll] New REVIEW invoice #{Num} (Id={Id}) — pushing to clients.",
                    inv.InvoiceNumber, inv.Id);

                await _hub.Clients.Group("review").SendAsync("newInvoice", new
                {
                    inv.Id,
                    inv.InvoiceNumber,
                    inv.VendorName,
                    inv.GrandTotal,
                    inv.Currency,
                    inv.ConfidenceScore,
                    inv.Region,
                    CreatedAt = inv.CreatedAt.ToString("o"),
                });
            }
        }

        _initialized = true;
    }
}
