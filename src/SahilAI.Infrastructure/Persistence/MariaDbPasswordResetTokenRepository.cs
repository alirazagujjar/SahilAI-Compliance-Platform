using System.Security.Cryptography;
using Dapper;
using MySqlConnector;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Infrastructure.Persistence;

public sealed class MariaDbPasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly string _cs;
    public MariaDbPasswordResetTokenRepository(string connectionString) => _cs = connectionString;

    public async Task<string> CreateAsync(int userId)
    {
        // Invalidate any previous unused tokens for this user
        const string invalidate = "UPDATE PasswordResetTokens SET IsUsed = 1 WHERE UserId = @UserId AND IsUsed = 0;";

        // 48 random bytes → 96 hex chars (URL-safe, no padding issues)
        var token     = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
        var expiresAt = DateTime.UtcNow.AddHours(1);

        const string insert = """
            INSERT INTO PasswordResetTokens (UserId, Token, ExpiresAt)
            VALUES (@UserId, @Token, @ExpiresAt);
            """;

        await using var conn = new MySqlConnection(_cs);
        await conn.ExecuteAsync(invalidate, new { UserId = userId });
        await conn.ExecuteAsync(insert, new { UserId = userId, Token = token, ExpiresAt = expiresAt });
        return token;
    }

    public async Task<PasswordResetToken?> GetValidAsync(string token)
    {
        const string sql = """
            SELECT Id, UserId, Token, ExpiresAt, IsUsed, CreatedAt
            FROM PasswordResetTokens
            WHERE Token = @Token AND IsUsed = 0 AND ExpiresAt > UTC_TIMESTAMP()
            LIMIT 1;
            """;
        await using var conn = new MySqlConnection(_cs);
        return await conn.QuerySingleOrDefaultAsync<PasswordResetToken>(sql, new { Token = token });
    }

    public async Task MarkUsedAsync(int id)
    {
        const string sql = "UPDATE PasswordResetTokens SET IsUsed = 1 WHERE Id = @Id;";
        await using var conn = new MySqlConnection(_cs);
        await conn.ExecuteAsync(sql, new { Id = id });
    }

    public async Task DeleteExpiredAsync()
    {
        const string sql = "DELETE FROM PasswordResetTokens WHERE ExpiresAt < UTC_TIMESTAMP() OR IsUsed = 1;";
        await using var conn = new MySqlConnection(_cs);
        await conn.ExecuteAsync(sql);
    }
}
