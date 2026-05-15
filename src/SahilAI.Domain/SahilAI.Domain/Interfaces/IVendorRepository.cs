using SahilAI.Domain.Entities;

namespace SahilAI.Domain.Interfaces;

public interface IVendorRepository
{
    Task UpsertAsync(Vendor vendor);
    Task<Vendor?> GetByTrnAsync(string taxRegNumber);
    Task<Vendor?> GetByIdAsync(int id);
    Task<IEnumerable<Vendor>> GetAllAsync();
    Task<IEnumerable<Vendor>> GetAllByTenantAsync(int tenantId);
    Task<int> InsertAsync(Vendor vendor);
    Task UpdateAsync(Vendor vendor);
    Task DeleteAsync(int id);
    Task MarkVerifiedAsync(int id);
}
