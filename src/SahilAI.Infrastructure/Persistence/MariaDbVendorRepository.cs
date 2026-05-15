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
            INSERT INTO Vendors (TenantId, Name, TaxRegNumber, Region, IsVerified, CreatedAt, UpdatedAt)
            VALUES (@TenantId, @Name, @TaxRegNumber, @Region, @IsVerified, @CreatedAt, @UpdatedAt)
            ON DUPLICATE KEY UPDATE
                Name      = VALUES(Name),
                TenantId  = VALUES(TenantId),
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

    public async Task<Vendor?> GetByIdAsync(int id)
    {
        const string sql = "SELECT * FROM Vendors WHERE Id = @Id LIMIT 1;";
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryFirstOrDefaultAsync<Vendor>(sql, new { Id = id });
    }

    public async Task<IEnumerable<Vendor>> GetAllAsync()
    {
        const string sql = "SELECT * FROM Vendors ORDER BY Name;";
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryAsync<Vendor>(sql);
    }

    public async Task<int> InsertAsync(Vendor vendor)
    {
        const string sql = """
            INSERT INTO Vendors (TenantId, Name, TaxRegNumber, Region, IsVerified, Email, Phone, Address, CreatedAt, UpdatedAt)
            VALUES (@TenantId, @Name, @TaxRegNumber, @Region, @IsVerified, @Email, @Phone, @Address, @CreatedAt, @UpdatedAt);
            SELECT LAST_INSERT_ID();
            """;
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.ExecuteScalarAsync<int>(sql, vendor);
    }

    public async Task<IEnumerable<Vendor>> GetAllByTenantAsync(int tenantId)
    {
        const string sql = "SELECT * FROM Vendors WHERE TenantId = @TenantId ORDER BY Name;";
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryAsync<Vendor>(sql, new { TenantId = tenantId });
    }

    public async Task UpdateAsync(Vendor vendor)
    {
        const string sql = """
            UPDATE Vendors
            SET Name          = @Name,
                TaxRegNumber  = @TaxRegNumber,
                Region        = @Region,
                Email         = @Email,
                Phone         = @Phone,
                Address       = @Address,
                IsVerified    = @IsVerified,
                UpdatedAt     = UTC_TIMESTAMP()
            WHERE Id = @Id AND TenantId = @TenantId;
            """;
        await using var conn = new MySqlConnection(_connectionString);
        await conn.ExecuteAsync(sql, vendor);
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = "DELETE FROM Vendors WHERE Id = @Id;";
        await using var conn = new MySqlConnection(_connectionString);
        await conn.ExecuteAsync(sql, new { Id = id });
    }

    public async Task MarkVerifiedAsync(int id)
    {
        const string sql = "UPDATE Vendors SET IsVerified = 1, UpdatedAt = UTC_TIMESTAMP() WHERE Id = @Id;";
        await using var conn = new MySqlConnection(_connectionString);
        await conn.ExecuteAsync(sql, new { Id = id });
    }
}
