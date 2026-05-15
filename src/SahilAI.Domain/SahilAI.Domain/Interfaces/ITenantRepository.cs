using SahilAI.Domain.Entities;

namespace SahilAI.Domain.Interfaces;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(int id);
    Task<Tenant?> GetBySlugAsync(string slug);
    Task<IEnumerable<Tenant>> GetAllAsync();
    Task<int> InsertAsync(Tenant tenant);
}
