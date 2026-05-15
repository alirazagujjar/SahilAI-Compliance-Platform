using Dapper;
using MySqlConnector;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Infrastructure.Persistence;

public sealed class MariaDbUserRepository : IUserRepository
{
    private readonly string _cs;
    public MariaDbUserRepository(string connectionString) => _cs = connectionString;

    public async Task<AppUser?> GetByEmailAsync(string email)
    {
        const string sql = """
            SELECT Id, TenantId, Email, PasswordHash, FullName, Role, IsActive, LastLoginAt, CreatedAt
            FROM Users WHERE Email = @Email AND IsActive = 1 LIMIT 1;
            """;
        await using var conn = new MySqlConnection(_cs);
        return await conn.QuerySingleOrDefaultAsync<AppUser>(sql, new { Email = email });
    }

    public async Task<AppUser?> GetByIdAsync(int id)
    {
        const string sql = """
            SELECT Id, TenantId, Email, PasswordHash, FullName, Role, IsActive, LastLoginAt, CreatedAt
            FROM Users WHERE Id = @Id LIMIT 1;
            """;
        await using var conn = new MySqlConnection(_cs);
        return await conn.QuerySingleOrDefaultAsync<AppUser>(sql, new { Id = id });
    }

    public async Task<IEnumerable<AppUser>> GetByTenantAsync(int tenantId)
    {
        const string sql = """
            SELECT Id, TenantId, Email, PasswordHash, FullName, Role, IsActive, LastLoginAt, CreatedAt
            FROM Users WHERE TenantId = @TenantId ORDER BY FullName;
            """;
        await using var conn = new MySqlConnection(_cs);
        return await conn.QueryAsync<AppUser>(sql, new { TenantId = tenantId });
    }

    public async Task<int> InsertAsync(AppUser user)
    {
        const string sql = """
            INSERT INTO Users (TenantId, Email, PasswordHash, FullName, Role, IsActive)
            VALUES (@TenantId, @Email, @PasswordHash, @FullName, @Role, @IsActive);
            SELECT LAST_INSERT_ID();
            """;
        await using var conn = new MySqlConnection(_cs);
        return await conn.ExecuteScalarAsync<int>(sql, user);
    }

    public async Task UpdateLastLoginAsync(int id)
    {
        const string sql = "UPDATE Users SET LastLoginAt = UTC_TIMESTAMP() WHERE Id = @Id;";
        await using var conn = new MySqlConnection(_cs);
        await conn.ExecuteAsync(sql, new { Id = id });
    }

    public async Task UpdatePasswordAsync(int id, string passwordHash)
    {
        const string sql = "UPDATE Users SET PasswordHash = @Hash WHERE Id = @Id;";
        await using var conn = new MySqlConnection(_cs);
        await conn.ExecuteAsync(sql, new { Hash = passwordHash, Id = id });
    }
}
