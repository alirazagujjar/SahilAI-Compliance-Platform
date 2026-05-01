using SahilAI.Domain.Entities;

namespace SahilAI.Domain.Interfaces;

public interface IValidationRulesRepository
{
    Task<IEnumerable<ValidationRule>> GetByRegionAsync(string region);
}
