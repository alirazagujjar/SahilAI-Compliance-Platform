using Dapper;
using MySqlConnector;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Infrastructure.Persistence;

public sealed class MariaDbVendorRepository : IVendorRepository
{
    private readonly string _connectionString;

    public MariaDbVendorRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task UpsertAsync(Vendor vendor)
    {
        const string sql = """
            INSERT INTO Vendors (Name, TaxRegNumber, Region, IsVerified, CreatedAt, UpdatedAt)
            VALUES (@Name, @TaxRegNumber, @Region, @IsVerified, @CreatedAt, @UpdatedAt)
            ON DUPLICATE KEY UPDATE
                Name      = VALUES(Name),
                UpdatedAt = VALUES(UpdatedAt);
            """;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.ExecuteAsync(sql, vendor);
    }

    public async Task<Vendor?> GetByTrnAsync(string taxRegNumber)
    {
        const string sql = "SELECT * FROM Vendors WHERE TaxRegNumber = @TaxRegNumber LIMIT 1;";
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryFirstOrDefaultAsync<Vendor>(sql, new { TaxRegNumber = taxRegNumber });
    }
}
