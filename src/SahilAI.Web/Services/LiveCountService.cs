using SahilAI.Domain.Entities;

namespace SahilAI.Web.Services;

/// <summary>
/// In-process singleton that caches the latest invoice status counts and review
/// queue. Blazor Server components subscribe to its C# events and call
/// InvokeAsync(StateHasChanged) — no JS/SignalR client connection required because
/// Blazor Server rendering already happens on the server.
/// </summary>
public sealed class LiveCountService
{
    // ── State ────────────────────────────────────────────────────────────────
    private volatile IReadOnlyDictionary<string, int> _counts
        = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private volatile IReadOnlyList<Invoice> _reviewQueue = [];

    // ── Public accessors ─────────────────────────────────────────────────────

    /// Latest counts per status key (e.g. "REVIEW", "VALID", …).
    public IReadOnlyDictionary<string, int> Counts => _counts;

    /// Current review-queue snapshot, ordered most-recent first.
    public IReadOnlyList<Invoice> ReviewQueue => _reviewQueue;

    /// Whether the service has received at least one poll from the background worker.
    public bool IsReady { get; private set; }

    // ── Events ───────────────────────────────────────────────────────────────

    /// Raised on the thread pool whenever any status count changes.
    public event Action? CountsChanged;

    /// Raised for each invoice that newly enters the REVIEW queue.
    public event Action<Invoice>? NewReviewInvoice;

    /// Raised when items are removed from the review queue (approved / rejected).
    public event Action? ReviewQueueChanged;

    // ── Internal setters (called only by InvoicePollingService) ─────────────

    internal void UpdateCounts(IReadOnlyDictionary<string, int> counts)
    {
        _counts  = counts;
        IsReady  = true;
        CountsChanged?.Invoke();
    }

    internal void UpdateReviewQueue(IReadOnlyList<Invoice> queue, IEnumerable<Invoice> newArrivals)
    {
        _reviewQueue = queue;

        var arrivals = newArrivals.ToList();
        if (arrivals.Count > 0)
        {
            foreach (var inv in arrivals)
                NewReviewInvoice?.Invoke(inv);
        }
        else
        {
            ReviewQueueChanged?.Invoke();
        }
    }
}
