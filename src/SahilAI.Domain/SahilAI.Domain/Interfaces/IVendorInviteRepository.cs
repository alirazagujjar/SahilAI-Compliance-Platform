using SahilAI.Domain.Entities;

namespace SahilAI.Domain.Interfaces;

public interface IVendorInviteRepository
{
    Task<string> CreateAsync(VendorInvite invite);
    Task<VendorInvite?> GetValidAsync(string token);
    Task MarkUsedAsync(int id);
    Task<IEnumerable<VendorInvite>> GetByTenantAsync(int tenantId);
}
