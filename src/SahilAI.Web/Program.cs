using SahilAI.Domain.Interfaces;
using SahilAI.Infrastructure;
using SahilAI.Infrastructure.Persistence;
using SahilAI.Web;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseAntiforgery();

// ── Serve original invoice files securely by database ID ────────────────────
app.MapGet("/api/files/{id:int}", async (int id, IInvoiceRepository invoices, IConfiguration config) =>
{
    var invoice = await invoices.GetByIdAsync(id);
    if (invoice is null) return Results.NotFound();

    var reviewPath  = config["Watcher:ReviewPath"]  ?? "processed/review";
    var successPath = config["Watcher:SuccessPath"] ?? "processed/success";
    var fileName    = Path.GetFileName(invoice.SourceFile ?? "");
    if (string.IsNullOrEmpty(fileName)) return Results.NotFound();

    var candidates = new[]
    {
        invoice.SourceFile ?? "",
        Path.Combine(reviewPath,  fileName),
        Path.Combine(successPath, fileName),
    };
    var filePath = candidates.FirstOrDefault(File.Exists);
    if (filePath is null) return Results.NotFound();

    var ext = Path.GetExtension(filePath).ToLowerInvariant();
    var contentType = ext switch
    {
        ".pdf"            => "application/pdf",
        ".png"            => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _                 => "text/plain; charset=utf-8",
    };

    return Results.File(filePath, contentType, enableRangeProcessing: true);
}).DisableAntiforgery();

// ── Init DB then start ───────────────────────────────────────────────────────
var dbInit = app.Services.GetRequiredService<DatabaseInitializer>();
await dbInit.InitializeAsync();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
