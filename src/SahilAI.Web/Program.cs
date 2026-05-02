using QRCoder;
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

// ── ZATCA QR code image (rendered PNG from stored TLV payload) ───────────────
app.MapGet("/api/qr/{id:int}", async (int id, IInvoiceRepository invoices) =>
{
    var invoice = await invoices.GetByIdAsync(id);
    if (invoice is null || string.IsNullOrEmpty(invoice.QrCodeBase64))
        return Results.NotFound();

    try
    {
        var tlvBytes = Convert.FromBase64String(invoice.QrCodeBase64);
        using var qrGenerator = new QRCodeGenerator();
        var qrData   = qrGenerator.CreateQrCode(tlvBytes, QRCodeGenerator.ECCLevel.M);
        // slate-900 on white — byte[] overload (hex strings not in QRCoder 1.6)
        var pngBytes = new PngByteQRCode(qrData).GetGraphic(10,
            new byte[] { 15, 23, 42, 255 },   // dark  — #0f172a (slate-900)
            new byte[] { 255, 255, 255, 255 }  // light — white
        );
        return Results.File(pngBytes, "image/png",
            $"zatca-qr-{invoice.InvoiceNumber.Replace("/", "-")}.png");
    }
    catch
    {
        return Results.Problem("QR generation failed.");
    }
}).DisableAntiforgery();

// ── ZATCA UBL 2.1 XML download ───────────────────────────────────────────────
app.MapGet("/api/xml/{id:int}", async (int id, IInvoiceRepository invoices) =>
{
    var invoice = await invoices.GetByIdAsync(id);
    if (invoice is null || string.IsNullOrEmpty(invoice.XmlPath)) return Results.NotFound();
    if (!File.Exists(invoice.XmlPath)) return Results.NotFound();

    var downloadName = $"{invoice.InvoiceNumber.Replace("/", "-").Replace(":", "-")}_{id}.xml";
    return Results.File(invoice.XmlPath, "application/xml", downloadName);
}).DisableAntiforgery();

// ── Init DB then start ───────────────────────────────────────────────────────
var dbInit = app.Services.GetRequiredService<DatabaseInitializer>();
await dbInit.InitializeAsync();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
