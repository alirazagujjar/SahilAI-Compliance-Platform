using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Infrastructure.Email;

public sealed class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendPasswordResetAsync(string toEmail, string toName, string resetLink)
    {
        var section     = _config.GetSection("Email");
        var host        = section["SmtpHost"]    ?? throw new InvalidOperationException("Email:SmtpHost not configured.");
        var port        = int.Parse(section["SmtpPort"] ?? "587");
        var enableSsl   = bool.Parse(section["EnableSsl"] ?? "true");
        var senderEmail = section["SenderEmail"] ?? "noreply@sahilai.com";
        var senderName  = section["SenderName"]  ?? "Sahil AI";
        var username    = section["Username"]    ?? string.Empty;
        var password    = section["Password"]    ?? string.Empty;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, senderEmail));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = "Reset your Sahil AI password";

        var body = new BodyBuilder
        {
            HtmlBody = BuildHtmlBody(toName, resetLink),
            TextBody = $"Hi {toName},\n\nReset your Sahil AI password by visiting:\n{resetLink}\n\nThis link expires in 1 hour. If you didn't request a reset, you can ignore this email.\n\n— Sahil AI Team"
        };
        message.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        var secureOption = enableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;

        try
        {
            await client.ConnectAsync(host, port, secureOption);
            if (!string.IsNullOrEmpty(username))
                await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            _logger.LogInformation("Password reset email sent to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", toEmail);
            throw;
        }
    }

    public async Task SendVendorInviteAsync(string toEmail, string companyName, string registerLink, string invitedByName)
    {
        var section     = _config.GetSection("Email");
        var host        = section["SmtpHost"]    ?? throw new InvalidOperationException("Email:SmtpHost not configured.");
        var port        = int.Parse(section["SmtpPort"] ?? "587");
        var enableSsl   = bool.Parse(section["EnableSsl"] ?? "true");
        var senderEmail = section["SenderEmail"] ?? "noreply@sahilai.com";
        var senderName  = section["SenderName"]  ?? "Sahil AI";
        var username    = section["Username"]    ?? string.Empty;
        var password    = section["Password"]    ?? string.Empty;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, senderEmail));
        message.To.Add(new MailboxAddress(companyName, toEmail));
        message.Subject = $"You've been invited to Sahil AI Vendor Portal";

        var body = new BodyBuilder
        {
            HtmlBody = BuildVendorInviteHtml(companyName, registerLink, invitedByName),
            TextBody = $"Hi,\n\n{invitedByName} has invited {companyName} to join Sahil AI Vendor Portal.\n\nRegister here:\n{registerLink}\n\nThis invite expires in 7 days.\n\n— Sahil AI Team"
        };
        message.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        var secureOption = enableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        try
        {
            await client.ConnectAsync(host, port, secureOption);
            if (!string.IsNullOrEmpty(username))
                await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            _logger.LogInformation("Vendor invite email sent to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send vendor invite to {Email}", toEmail);
            throw;
        }
    }

    private static string BuildVendorInviteHtml(string companyName, string registerLink, string invitedBy) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="margin:0;padding:0;background:#0f172a;font-family:Inter,ui-sans-serif,system-ui,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#0f172a;padding:40px 16px;">
            <tr><td align="center">
              <table width="100%" style="max-width:520px;background:#1e293b;border-radius:16px;border:1px solid #334155;overflow:hidden;">
                <tr>
                  <td style="background:linear-gradient(135deg,#4f46e5,#7c3aed);padding:32px;text-align:center;">
                    <div style="display:inline-block;width:52px;height:52px;background:rgba(255,255,255,0.15);border-radius:14px;
                                line-height:52px;font-size:20px;font-weight:800;color:#fff;margin-bottom:12px;">SA</div>
                    <p style="margin:0;color:#e0e7ff;font-size:14px;font-weight:500;">SAHIL AI · VENDOR PORTAL</p>
                  </td>
                </tr>
                <tr>
                  <td style="padding:36px 32px;">
                    <h1 style="margin:0 0 8px;font-size:22px;font-weight:700;color:#f1f5f9;">You're invited!</h1>
                    <p style="margin:0 0 24px;font-size:14px;color:#94a3b8;line-height:1.6;">
                      <strong style="color:#e2e8f0;">{System.Net.WebUtility.HtmlEncode(invitedBy)}</strong> has invited
                      <strong style="color:#e2e8f0;">{System.Net.WebUtility.HtmlEncode(companyName)}</strong>
                      to the Sahil AI Vendor Portal — a secure platform to submit and track invoices for ZATCA compliance.
                    </p>
                    <table width="100%" cellpadding="0" cellspacing="0">
                      <tr>
                        <td align="center" style="padding:4px 0 28px;">
                          <a href="{System.Net.WebUtility.HtmlEncode(registerLink)}"
                             style="display:inline-block;background:#4f46e5;color:#fff;text-decoration:none;
                                    font-size:14px;font-weight:600;padding:13px 32px;border-radius:10px;
                                    box-shadow:0 4px 24px rgba(79,70,229,0.4);">
                            Create Your Account
                          </a>
                        </td>
                      </tr>
                    </table>
                    <div style="background:#0f172a;border-radius:10px;padding:14px 16px;margin-bottom:24px;">
                      <p style="margin:0;font-size:12px;color:#64748b;line-height:1.5;">
                        ⏱ This invite expires in <strong style="color:#94a3b8;">7 days</strong>.
                      </p>
                    </div>
                    <p style="margin:0;font-size:11px;color:#475569;line-height:1.6;">
                      Or copy this link:<br>
                      <a href="{System.Net.WebUtility.HtmlEncode(registerLink)}" style="color:#6366f1;word-break:break-all;">{System.Net.WebUtility.HtmlEncode(registerLink)}</a>
                    </p>
                  </td>
                </tr>
                <tr>
                  <td style="border-top:1px solid #334155;padding:20px 32px;text-align:center;">
                    <p style="margin:0;font-size:11px;color:#475569;">Sahil AI &mdash; Agentic Compliance Platform &middot; ZATCA Phase 2</p>
                  </td>
                </tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    private static string BuildHtmlBody(string name, string resetLink) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="margin:0;padding:0;background:#0f172a;font-family:Inter,ui-sans-serif,system-ui,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#0f172a;padding:40px 16px;">
            <tr><td align="center">
              <table width="100%" style="max-width:520px;background:#1e293b;border-radius:16px;border:1px solid #334155;overflow:hidden;">

                <!-- Header -->
                <tr>
                  <td style="background:linear-gradient(135deg,#4f46e5,#7c3aed);padding:32px;text-align:center;">
                    <div style="display:inline-block;width:52px;height:52px;background:rgba(255,255,255,0.15);border-radius:14px;
                                line-height:52px;font-size:20px;font-weight:800;color:#fff;margin-bottom:12px;">SA</div>
                    <p style="margin:0;color:#e0e7ff;font-size:14px;font-weight:500;letter-spacing:0.05em;">SAHIL AI</p>
                  </td>
                </tr>

                <!-- Body -->
                <tr>
                  <td style="padding:36px 32px;">
                    <h1 style="margin:0 0 8px;font-size:22px;font-weight:700;color:#f1f5f9;">Reset your password</h1>
                    <p style="margin:0 0 24px;font-size:14px;color:#94a3b8;line-height:1.6;">
                      Hi {System.Net.WebUtility.HtmlEncode(name)}, we received a request to reset the password for your Sahil AI account.
                      Click the button below to choose a new password.
                    </p>

                    <!-- CTA Button -->
                    <table width="100%" cellpadding="0" cellspacing="0">
                      <tr>
                        <td align="center" style="padding:4px 0 28px;">
                          <a href="{System.Net.WebUtility.HtmlEncode(resetLink)}"
                             style="display:inline-block;background:#4f46e5;color:#fff;text-decoration:none;
                                    font-size:14px;font-weight:600;padding:13px 32px;border-radius:10px;
                                    box-shadow:0 4px 24px rgba(79,70,229,0.4);">
                            Reset Password
                          </a>
                        </td>
                      </tr>
                    </table>

                    <!-- Expiry note -->
                    <div style="background:#0f172a;border-radius:10px;padding:14px 16px;margin-bottom:24px;">
                      <p style="margin:0;font-size:12px;color:#64748b;line-height:1.5;">
                        ⏱ This link expires in <strong style="color:#94a3b8;">1 hour</strong>.
                        If you didn't request a password reset, you can safely ignore this email — your account won't change.
                      </p>
                    </div>

                    <!-- Fallback link -->
                    <p style="margin:0;font-size:11px;color:#475569;line-height:1.6;">
                      If the button doesn't work, copy and paste this URL into your browser:<br>
                      <a href="{System.Net.WebUtility.HtmlEncode(resetLink)}"
                         style="color:#6366f1;word-break:break-all;">{System.Net.WebUtility.HtmlEncode(resetLink)}</a>
                    </p>
                  </td>
                </tr>

                <!-- Footer -->
                <tr>
                  <td style="border-top:1px solid #334155;padding:20px 32px;text-align:center;">
                    <p style="margin:0;font-size:11px;color:#475569;">
                      Sahil AI &mdash; Agentic Compliance Platform &middot; ZATCA Phase 2
                    </p>
                  </td>
                </tr>

              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
}
