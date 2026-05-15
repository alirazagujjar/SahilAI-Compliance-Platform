using Dapper;
using MySqlConnector;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Infrastructure.Persistence;

public sealed class MariaDbTenantRepository : ITenantRepository
{
    private readonly string _cs;
    public MariaDbTenantRepository(string connectionString) => _cs = connectionString;

    public async Task<Tenant?> GetByIdAsync(int id)
    {
        const string sql = "SELECT Id, Name, Slug, Plan, IsActive, CreatedAt FROM Tenants WHERE Id = @Id LIMIT 1;";
        await using var conn = new MySqlConnection(_cs);
        return await conn.QuerySingleOrDefaultAsync<Tenant>(sql, new { Id = id });
    }

    public async Task<Tenant?> GetBySlugAsync(string slug)
    {
        const string sql = "SELECT Id, Name, Slug, Plan, IsActive, CreatedAt FROM Tenants WHERE Slug = @Slug LIMIT 1;";
        await using var conn = new MySqlConnection(_cs);
        return await conn.QuerySingleOrDefaultAsync<Tenant>(sql, new { Slug = slug });
    }

    public async Task<IEnumerable<Tenant>> GetAllAsync()
    {
        const string sql = "SELECT Id, Name, Slug, Plan, IsActive, CreatedAt FROM Tenants ORDER BY Name;";
        await using var conn = new MySqlConnection(_cs);
        return await conn.QueryAsync<Tenant>(sql);
    }

    public async Task<int> InsertAsync(Tenant tenant)
    {
        const string sql = """
            INSERT INTO Tenants (Name, Slug, Plan, IsActive)
            VALUES (@Name, @Slug, @Plan, @IsActive);
            SELECT LAST_INSERT_ID();
            """;
        await using var conn = new MySqlConnection(_cs);
        return await conn.ExecuteScalarAsync<int>(sql, tenant);
    }
}
