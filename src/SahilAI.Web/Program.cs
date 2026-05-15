using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using QRCoder;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;
using SahilAI.Infrastructure;
using SahilAI.Infrastructure.Persistence;
using SahilAI.Web;
using SahilAI.Web.Hubs;
using SahilAI.Web.Services;
using BC = BCrypt.Net.BCrypt;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath    = "/login";
        opt.LogoutPath   = "/api/auth/logout";
        opt.ExpireTimeSpan = TimeSpan.FromHours(8);
        opt.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddInfrastructure(builder.Configuration);

// ── Real-time / SignalR ──────────────────────────────────────────────────────
builder.Services.AddSignalR();
builder.Services.AddSingleton<LiveCountService>();
builder.Services.AddHostedService<InvoicePollingService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ── SignalR hub ──────────────────────────────────────────────────────────────
app.MapHub<InvoiceHub>("/hubs/invoice");

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

// ── Auth endpoints ───────────────────────────────────────────────────────────
app.MapPost("/api/auth/login", async (HttpContext ctx, IUserRepository users) =>
{
    var form  = await ctx.Request.ReadFormAsync();
    var email = form["email"].ToString().Trim();
    var pass  = form["password"].ToString();

    var user = await users.GetByEmailAsync(email);
    if (user is null || !BC.Verify(pass, user.PasswordHash))
    {
        ctx.Response.Redirect("/login?error=1");
        return;
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name,           user.FullName),
        new(ClaimTypes.Email,          user.Email),
        new(ClaimTypes.Role,           user.Role),
        new("tenant_id",               user.TenantId.ToString()),
        new("vendor_id",               user.VendorId?.ToString() ?? ""),
    };
    var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    await users.UpdateLastLoginAsync(user.Id);

    // Vendors go to their portal; everyone else to the main dashboard
    var destination = user.Role == UserRole.Vendor ? "/vendor" : "/";
    ctx.Response.Redirect(destination);
}).DisableAntiforgery();

app.MapGet("/api/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    ctx.Response.Redirect("/login");
}).DisableAntiforgery();

// ── Vendor invite (admin sends invite email) ─────────────────────────────────
app.MapPost("/api/vendor/invite", async (
    HttpContext ctx,
    IVendorInviteRepository invites,
    IUserRepository users,
    IEmailService email) =>
{
    if (ctx.User.Identity?.IsAuthenticated != true ||
        ctx.User.FindFirst(ClaimTypes.Role)?.Value != UserRole.Admin)
    {
        ctx.Response.StatusCode = 403;
        return;
    }

    var form        = await ctx.Request.ReadFormAsync();
    var toEmail     = form["email"].ToString().Trim();
    var company     = form["company"].ToString().Trim();
    var inviterName = ctx.User.Identity.Name ?? "Admin";
    var inviterId   = int.Parse(ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    var tenantId    = int.Parse(ctx.User.FindFirst("tenant_id")?.Value ?? "1");

    var invite = new VendorInvite
    {
        Email       = toEmail,
        CompanyName = company,
        TenantId    = tenantId,
        InvitedById = inviterId,
    };

    var token        = await invites.CreateAsync(invite);
    var baseUrl      = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    var registerLink = $"{baseUrl}/vendor/register?token={token}";

    try { await email.SendVendorInviteAsync(toEmail, company, registerLink, inviterName); }
    catch { /* email failure logged inside service */ }

    ctx.Response.Redirect("/vendors?invited=1");
}).DisableAntiforgery();

// ── Vendor self-registration (via invite link) ────────────────────────────────
app.MapPost("/api/vendor/register", async (
    HttpContext ctx,
    IVendorInviteRepository invites,
    IVendorRepository vendors,
    IUserRepository users) =>
{
    var form        = await ctx.Request.ReadFormAsync();
    var token       = form["token"].ToString().Trim();
    var fullName    = form["fullName"].ToString().Trim();
    var companyName = form["companyName"].ToString().Trim();
    var trn         = form["trn"].ToString().Trim();
    var password    = form["password"].ToString();
    var confirm     = form["confirm"].ToString();

    if (password != confirm || password.Length < 8)
    {
        ctx.Response.Redirect($"/vendor/register?token={Uri.EscapeDataString(token)}&error=mismatch");
        return;
    }

    var invite = await invites.GetValidAsync(token);
    if (invite is null)
    {
        ctx.Response.Redirect("/vendor/register?error=expired");
        return;
    }

    // Create or find vendor record
    var existingVendor = await vendors.GetByTrnAsync(trn);
    int vendorId;
    if (existingVendor is not null)
    {
        vendorId = existingVendor.Id;
        await vendors.MarkVerifiedAsync(vendorId);
    }
    else
    {
        vendorId = await vendors.InsertAsync(new Vendor
        {
            TenantId      = invite.TenantId,
            Name          = companyName,
            TaxRegNumber  = trn,
            Region        = "SA",
            IsVerified    = true,
            Email         = invite.Email,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        });
    }

    // Create user account
    var hash = BC.HashPassword(password, workFactor: 11);
    var newUser = new AppUser
    {
        TenantId     = invite.TenantId,
        VendorId     = vendorId,
        Email        = invite.Email,
        PasswordHash = hash,
        FullName     = fullName,
        Role         = UserRole.Vendor,
        IsActive     = true,
    };
    var userId = await users.InsertAsync(newUser);
    await invites.MarkUsedAsync(invite.Id);

    // Sign the new user in immediately
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, userId.ToString()),
        new(ClaimTypes.Name,           fullName),
        new(ClaimTypes.Email,          invite.Email),
        new(ClaimTypes.Role,           UserRole.Vendor),
        new("tenant_id",               invite.TenantId.ToString()),
        new("vendor_id",               vendorId.ToString()),
    };
    var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    ctx.Response.Redirect("/vendor");
}).DisableAntiforgery();

// ── Forgot password ──────────────────────────────────────────────────────────
app.MapPost("/api/auth/forgot-password", async (
    HttpContext ctx,
    IUserRepository users,
    IPasswordResetTokenRepository tokens,
    IEmailService email,
    IConfiguration config) =>
{
    var form      = await ctx.Request.ReadFormAsync();
    var userEmail = form["email"].ToString().Trim().ToLowerInvariant();

    // Always show the same success message to prevent email enumeration
    var successRedirect = "/forgot-password?sent=1";

    var user = await users.GetByEmailAsync(userEmail);
    if (user is not null && user.IsActive)
    {
        try
        {
            var token     = await tokens.CreateAsync(user.Id);
            var baseUrl   = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var resetLink = $"{baseUrl}/reset-password?token={token}";
            await email.SendPasswordResetAsync(user.Email, user.FullName, resetLink);
        }
        catch
        {
            // Log already handled in SmtpEmailService; don't leak errors to the user
        }
    }

    ctx.Response.Redirect(successRedirect);
}).DisableAntiforgery();

// ── Reset password ───────────────────────────────────────────────────────────
app.MapPost("/api/auth/reset-password", async (
    HttpContext ctx,
    IUserRepository users,
    IPasswordResetTokenRepository tokens) =>
{
    var form     = await ctx.Request.ReadFormAsync();
    var token    = form["token"].ToString().Trim();
    var password = form["password"].ToString();
    var confirm  = form["confirm"].ToString();

    if (string.IsNullOrEmpty(token))
    {
        ctx.Response.Redirect("/forgot-password?error=invalid");
        return;
    }

    if (password != confirm || password.Length < 8)
    {
        ctx.Response.Redirect($"/reset-password?token={Uri.EscapeDataString(token)}&error=mismatch");
        return;
    }

    var record = await tokens.GetValidAsync(token);
    if (record is null)
    {
        ctx.Response.Redirect("/reset-password?error=expired");
        return;
    }

    var hash = BC.HashPassword(password, workFactor: 11);
    await users.UpdatePasswordAsync(record.UserId, hash);
    await tokens.MarkUsedAsync(record.Id);

    ctx.Response.Redirect("/login?reset=1");
}).DisableAntiforgery();

// ── Init DB then start ───────────────────────────────────────────────────────
var dbInit = app.Services.GetRequiredService<DatabaseInitializer>();
await dbInit.InitializeAsync();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
