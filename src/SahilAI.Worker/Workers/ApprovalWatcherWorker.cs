using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SahilAI.Domain.Interfaces;
using SahilAI.Worker.Configuration;

namespace SahilAI.Worker.Workers;

/// Polls the database every 30 seconds for invoices that a human has approved
/// (IsApproved = 1, Status = REVIEW) and promotes them to the success folder.
public sealed class ApprovalWatcherWorker : BackgroundService
{
    private readonly IInvoiceRepository _invoices;
    private readonly WatcherOptions _options;
    private readonly ILogger<ApprovalWatcherWorker> _logger;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public ApprovalWatcherWorker(
        IInvoiceRepository invoices,
        IOptions<WatcherOptions> options,
        ILogger<ApprovalWatcherWorker> logger)
    {
        _invoices = invoices;
        _options  = options.Value;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "HITL Approval Worker started — polling every {Seconds}s for approved invoices.",
            PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessApprovedInvoicesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Approval watcher encountered an unexpected error.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessApprovedInvoicesAsync(CancellationToken ct)
    {
        var approved = (await _invoices.GetPendingApprovalAsync()).ToList();
        if (approved.Count == 0) return;

        _logger.LogInformation("Found {Count} human-approved invoice(s) pending promotion.", approved.Count);

        foreach (var invoice in approved)
        {
            if (ct.IsCancellationRequested) break;

            _logger.LogInformation(
                "Processing approval: Invoice #{Number} (Id={Id}, ApprovedBy={Who})",
                invoice.InvoiceNumber, invoice.Id, invoice.ApprovedBy ?? "unknown");

            // Attempt to move the file from review → success
            var moved = TryPromoteFile(invoice.SourceFile);

            // Update DB status regardless of file outcome — the human decision is the source of truth
            await _invoices.MarkApprovedAsync(invoice.Id, "ApprovalWatcherWorker");

            if (moved)
            {
                _logger.LogInformation(
                    "[APPROVED] Invoice #{Number} (Id={Id}) promoted to success folder.",
                    invoice.InvoiceNumber, invoice.Id);
            }
            else
            {
                _logger.LogWarning(
                    "[APPROVED] Invoice #{Number} (Id={Id}) status updated but source file '{File}' " +
                    "was not found in the review folder — it may have already been moved manually.",
                    invoice.InvoiceNumber, invoice.Id, invoice.SourceFile);
            }
        }
    }

    private bool TryPromoteFile(string sourceFileName)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName)) return false;

        // The file in the review folder might have a timestamp suffix if there was a name collision
        // when it was first moved. Search by original name prefix to handle both cases.
        var nameWithoutExt = Path.GetFileNameWithoutExtension(sourceFileName);
        var ext            = Path.GetExtension(sourceFileName);

        var candidates = Directory.GetFiles(_options.ReviewPath, $"{nameWithoutExt}*{ext}");
        if (candidates.Length == 0) return false;

        // Pick the most recently modified match
        var reviewFile  = candidates.OrderByDescending(File.GetLastWriteTimeUtc).First();
        var fileName    = Path.GetFileName(reviewFile);
        var destination = Path.Combine(_options.SuccessPath, fileName);

        // Avoid overwrite in success folder
        if (File.Exists(destination))
        {
            var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            destination = Path.Combine(_options.SuccessPath,
                $"{Path.GetFileNameWithoutExtension(fileName)}_{ts}{ext}");
        }

        try
        {
            File.Move(reviewFile, destination);
            _logger.LogInformation("Moved '{File}' → {Dest}", fileName, _options.SuccessPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move '{File}' to success folder.", fileName);
            return false;
        }
    }
}
