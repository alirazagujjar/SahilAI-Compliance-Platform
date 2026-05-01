using Dapper;
using MySqlConnector;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Infrastructure.Persistence;

public sealed class MariaDbValidationRulesRepository : IValidationRulesRepository
{
    private readonly string _connectionString;

    public MariaDbValidationRulesRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<ValidationRule>> GetByRegionAsync(string region)
    {
        const string sql = """
            SELECT * FROM ValidationRules
            WHERE Region = @Region AND IsActive = 1
            ORDER BY Id;
            """;
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryAsync<ValidationRule>(sql, new { Region = region });
    }
}
