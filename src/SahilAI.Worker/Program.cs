using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using SahilAI.Infrastructure;
using SahilAI.Infrastructure.Persistence;
using SahilAI.Worker.Configuration;
using SahilAI.Worker.Workers;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/sahilai-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("===========================================");
    Log.Information("  Sahil AI — Agentic Compliance Platform  ");
    Log.Information("  Version 1.0 | Target: UAE/SA/USA        ");
    Log.Information("===========================================");

    var host = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((_, config) =>
        {
            config.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        })
        .UseSerilog()
        .ConfigureServices((ctx, services) =>
        {
            services.AddInfrastructure(ctx.Configuration);
            services.Configure<WatcherOptions>(ctx.Configuration.GetSection("Watcher"));
            services.AddHostedService<InvoiceWatcherWorker>();
            services.AddHostedService<ApprovalWatcherWorker>();
        })
        .Build();

    var dbInit = host.Services.GetRequiredService<DatabaseInitializer>();
    await dbInit.InitializeAsync();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Sahil AI terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
