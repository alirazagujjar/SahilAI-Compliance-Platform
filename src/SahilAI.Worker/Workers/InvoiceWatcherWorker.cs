using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SahilAI.Application.Interfaces;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;
using SahilAI.Infrastructure.Documents;
using SahilAI.Worker.Configuration;

namespace SahilAI.Worker.Workers;

public sealed class InvoiceWatcherWorker : BackgroundService
{
    private readonly IInvoiceProcessingService _processingService;
    private readonly IProcessingQueueRepository _queue;
    private readonly WatcherOptions _options;
    private readonly ILogger<InvoiceWatcherWorker> _logger;
    private FileSystemWatcher? _watcher;

    public InvoiceWatcherWorker(
        IInvoiceProcessingService processingService,
        IProcessingQueueRepository queue,
        IOptions<WatcherOptions> options,
        ILogger<InvoiceWatcherWorker> logger)
    {
        _processingService = processingService;
        _queue   = queue;
        _options = options.Value;
        _logger  = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureDirectories();

        // Watch all files — filter by supported extension in the handler
        _watcher = new FileSystemWatcher(_options.InboxPath)
        {
            Filter           = "*.*",
            NotifyFilter     = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileCreated;

        var supported = string.Join(", ", DocumentReaderFactory.SupportedExtensions);
        _logger.LogInformation("Watching for invoices in: {Path}", _options.InboxPath);
        _logger.LogInformation("Supported formats: {Formats}", supported);
        _logger.LogInformation("Confidence threshold: {Threshold:P0}", _options.ConfidenceThreshold);
        _logger.LogInformation("Success        → {SuccessPath}",       _options.SuccessPath);
        _logger.LogInformation("Review         → {ReviewPath}",        _options.ReviewPath);
        _logger.LogInformation("Invalid Format → {InvalidFormatPath}", _options.InvalidFormatPath);
        _logger.LogInformation("Drop any invoice file into the inbox to begin processing.");

        stoppingToken.Register(() =>
        {
            _logger.LogInformation("Watcher shutting down.");
            _watcher?.Dispose();
        });

        return Task.CompletedTask;
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        Task.Run(async () =>
        {
            await Task.Delay(500);

            var ext = Path.GetExtension(e.Name ?? "").ToLowerInvariant();
            if (!DocumentReaderFactory.SupportedExtensions.Contains(ext))
            {
                _logger.LogWarning("Unsupported format '{Ext}' — moving {File} to invalidformat folder.", ext, e.Name);

                await _queue.EnqueueAsync(new ProcessingQueueItem
                {
                    FileName    = e.Name!,
                    FilePath    = e.FullPath,
                    Region      = _options.DefaultRegion,
                    FileType    = ext.TrimStart('.').ToUpperInvariant(),
                    QueueStatus = QueueStatus.InvalidFormat
                });

                MoveFile(e.FullPath, _options.InvalidFormatPath, e.Name!);
                return;
            }

            _logger.LogInformation("New file detected: {File} ({Ext})", e.Name, ext);

            var result = await _processingService.ProcessDocumentAsync(e.FullPath, _options.DefaultRegion);

            if (result.Success)
            {
                string destination;
                string label;

                if (result.Status == InvoiceStatus.NotSupportedFormat)
                {
                    destination = _options.InvalidFormatPath;
                    label       = "INVALID FORMAT — LLM does not support this file type";
                }
                else if (result.ConfidenceScore < _options.ConfidenceThreshold)
                {
                    destination = _options.ReviewPath;
                    label       = "REVIEW — awaiting human approval";
                }
                else
                {
                    destination = _options.SuccessPath;
                    label       = "SUCCESS";
                }

                _logger.LogInformation(
                    "[{Status}] Invoice Id={Id} | Confidence={Score:P0} | Anomalies={Count} → {Label}",
                    result.Status, result.InvoiceId, result.ConfidenceScore, result.Anomalies.Count, label);

                foreach (var anomaly in result.Anomalies)
                    _logger.LogWarning("  >> {Anomaly}", anomaly);

                MoveFile(e.FullPath, destination, e.Name!);
            }
            else
            {
                _logger.LogError("[{Status}] Processing failed: {Message}", result.Status, result.Message);
            }
        });
    }

    private void MoveFile(string sourcePath, string destinationFolder, string fileName)
    {
        try
        {
            var destFile = Path.Combine(destinationFolder, fileName);

            // Avoid overwrite — append timestamp if file already exists
            if (File.Exists(destFile))
            {
                var ext  = Path.GetExtension(fileName);
                var name = Path.GetFileNameWithoutExtension(fileName);
                destFile = Path.Combine(destinationFolder, $"{name}_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}");
            }

            File.Move(sourcePath, destFile);
            _logger.LogInformation("Moved {File} → {Dest}", fileName, destinationFolder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move {File} to {Dest}", fileName, destinationFolder);
        }
    }

    private void EnsureDirectories()
    {
        foreach (var path in new[] { _options.InboxPath, _options.SuccessPath, _options.ReviewPath, _options.InvalidFormatPath, _options.XmlOutputPath })
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                _logger.LogInformation("Created directory: {Path}", path);
            }
        }
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }
}
