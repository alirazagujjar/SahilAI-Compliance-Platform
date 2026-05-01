using SahilAI.Domain.Entities;

namespace SahilAI.Domain.Interfaces;

public interface IVendorRepository
{
    Task UpsertAsync(Vendor vendor);
    Task<Vendor?> GetByTrnAsync(string taxRegNumber);
}
