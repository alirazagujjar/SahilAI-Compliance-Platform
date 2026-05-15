namespace SahilAI.Domain.Interfaces;

public interface IEmailService
{
    Task SendPasswordResetAsync(string toEmail, string toName, string resetLink);
    Task SendVendorInviteAsync(string toEmail, string companyName, string registerLink, string invitedByName);
}
