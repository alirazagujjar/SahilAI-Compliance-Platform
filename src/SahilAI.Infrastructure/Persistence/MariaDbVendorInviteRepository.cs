using System.Security.Cryptography;
using Dapper;
using MySqlConnector;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Infrastructure.Persistence;

public sealed class MariaDbVendorInviteRepository : IVendorInviteRepository
{
    private readonly string _cs;
    public MariaDbVendorInviteRepository(string connectionString) => _cs = connectionString;

    public async Task<string> CreateAsync(VendorInvite invite)
    {
        invite.Token     = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
        invite.ExpiresAt = DateTime.UtcNow.AddDays(7);

        const string sql = """
            INSERT INTO VendorInvites (Email, Token, TenantId, CompanyName, IsUsed, ExpiresAt, InvitedById)
            VALUES (@Email, @Token, @TenantId, @CompanyName, 0, @ExpiresAt, @InvitedById);
            """;
        await using var conn = new MySqlConnection(_cs);
        await conn.ExecuteAsync(sql, invite);
        return invite.Token;
    }

    public async Task<VendorInvite?> GetValidAsync(string token)
    {
        const string sql = """
            SELECT Id, Email, Token, TenantId, CompanyName, IsUsed, ExpiresAt, InvitedById, CreatedAt
            FROM VendorInvites
            WHERE Token = @Token AND IsUsed = 0 AND ExpiresAt > UTC_TIMESTAMP()
            LIMIT 1;
            """;
        await using var conn = new MySqlConnection(_cs);
        return await conn.QuerySingleOrDefaultAsync<VendorInvite>(sql, new { Token = token });
    }

    public async Task MarkUsedAsync(int id)
    {
        const string sql = "UPDATE VendorInvites SET IsUsed = 1 WHERE Id = @Id;";
        await using var conn = new MySqlConnection(_cs);
        await conn.ExecuteAsync(sql, new { Id = id });
    }

    public async Task<IEnumerable<VendorInvite>> GetByTenantAsync(int tenantId)
    {
        const string sql = """
            SELECT Id, Email, Token, TenantId, CompanyName, IsUsed, ExpiresAt, InvitedById, CreatedAt
            FROM VendorInvites WHERE TenantId = @TenantId ORDER BY CreatedAt DESC;
            """;
        await using var conn = new MySqlConnection(_cs);
        return await conn.QueryAsync<VendorInvite>(sql, new { TenantId = tenantId });
    }
}
