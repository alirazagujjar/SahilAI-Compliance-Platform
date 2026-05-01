using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SahilAI.Application.Interfaces;
using SahilAI.Application.Services;
using SahilAI.Domain.Documents;
using SahilAI.Domain.Interfaces;
using SahilAI.Infrastructure.AI;
using SahilAI.Infrastructure.Documents;
using SahilAI.Infrastructure.Persistence;

namespace SahilAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("MariaDb")
            ?? throw new InvalidOperationException("MariaDb connection string is missing.");

        var aiSection = config.GetSection("AI");
        var modelId = aiSection["ModelId"] ?? "llama3";
        var endpoint = aiSection["Endpoint"] ?? "http://localhost:11434";
        var apiKey = aiSection["ApiKey"] ?? "ollama";

        services.AddSingleton(sp =>
        {
            var builder = Kernel.CreateBuilder();

            builder.AddOpenAIChatCompletion(
                modelId: modelId,
                apiKey: apiKey,
                httpClient: new HttpClient { BaseAddress = new Uri(endpoint) }
            );

            return builder.Build();
        });

        services.AddSingleton<IDocumentReaderFactory, DocumentReaderFactory>();
        services.AddSingleton<IComplianceAgent, SemanticKernelComplianceAgent>();
        services.AddSingleton<IInvoiceRepository>(_ => new MariaDbInvoiceRepository(connectionString));
        services.AddSingleton<IAuditLogRepository>(_ => new MariaDbAuditLogRepository(connectionString));
        services.AddSingleton<IInvoiceLineItemRepository>(_ => new MariaDbInvoiceLineItemRepository(connectionString));
        services.AddSingleton<IVendorRepository>(_ => new MariaDbVendorRepository(connectionString));
        services.AddSingleton<IProcessingQueueRepository>(_ => new MariaDbProcessingQueueRepository(connectionString));
        services.AddSingleton<IValidationRulesRepository>(_ => new MariaDbValidationRulesRepository(connectionString));

        // Phase 2 compliance services
        services.AddSingleton<IZatcaComplianceService, ZatcaComplianceService>();
        services.AddSingleton<RegionalValidationService>();

        services.AddSingleton<IInvoiceProcessingService>(sp =>
        {
            var watcherSection   = config.GetSection("Watcher");
            var xmlOutputPath    = watcherSection["XmlOutputPath"]   ?? "processed/xml";
            var successPath      = watcherSection["SuccessPath"]     ?? "processed/success";
            var threshold        = decimal.TryParse(watcherSection["ConfidenceThreshold"], out var t) ? t : 0.90m;

            return new InvoiceProcessingService(
                sp.GetRequiredService<IComplianceAgent>(),
                sp.GetRequiredService<IInvoiceRepository>(),
                sp.GetRequiredService<IAuditLogRepository>(),
                sp.GetRequiredService<IInvoiceLineItemRepository>(),
                sp.GetRequiredService<IVendorRepository>(),
                sp.GetRequiredService<IProcessingQueueRepository>(),
                sp.GetRequiredService<IDocumentReaderFactory>(),
                sp.GetRequiredService<IZatcaComplianceService>(),
                sp.GetRequiredService<RegionalValidationService>(),
                sp.GetRequiredService<ILogger<InvoiceProcessingService>>(),
                xmlOutputPath,
                threshold,
                successPath);
        });

        services.AddSingleton<IReviewApprovalService>(sp =>
        {
            var w = config.GetSection("Watcher");
            return new ReviewApprovalService(
                sp.GetRequiredService<IInvoiceRepository>(),
                sp.GetRequiredService<IInvoiceLineItemRepository>(),
                sp.GetRequiredService<IZatcaComplianceService>(),
                sp.GetRequiredService<ILogger<ReviewApprovalService>>(),
                w["XmlOutputPath"] ?? "processed/xml",
                w["SuccessPath"]   ?? "processed/success",
                w["ReviewPath"]    ?? "processed/review");
        });

        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DatabaseInitializer>>();
            return new DatabaseInitializer(connectionString, logger);
        });

        return services;
    }
}
