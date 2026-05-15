using SahilAI.Domain.Entities;

namespace SahilAI.Domain.Interfaces;

public interface IUserRepository
{
    Task<AppUser?> GetByEmailAsync(string email);
    Task<AppUser?> GetByIdAsync(int id);
    Task<IEnumerable<AppUser>> GetByTenantAsync(int tenantId);
    Task<int> InsertAsync(AppUser user);
    Task UpdateLastLoginAsync(int id);
    Task UpdatePasswordAsync(int id, string passwordHash);
}
