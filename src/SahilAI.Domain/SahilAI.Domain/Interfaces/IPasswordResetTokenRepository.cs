using SahilAI.Domain.Entities;

namespace SahilAI.Domain.Interfaces;

public interface IPasswordResetTokenRepository
{
    Task<string> CreateAsync(int userId);
    Task<PasswordResetToken?> GetValidAsync(string token);
    Task MarkUsedAsync(int id);
    Task DeleteExpiredAsync();
}
