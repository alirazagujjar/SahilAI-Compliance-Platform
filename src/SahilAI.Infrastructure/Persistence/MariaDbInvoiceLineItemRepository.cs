using Dapper;
using MySqlConnector;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Infrastructure.Persistence;

public sealed class MariaDbInvoiceLineItemRepository : IInvoiceLineItemRepository
{
    private readonly string _connectionString;

    public MariaDbInvoiceLineItemRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InsertBatchAsync(IEnumerable<InvoiceLineItem> items)
    {
        const string sql = """
            INSERT INTO InvoiceLineItems (InvoiceId, LineNumber, Description, Quantity, UnitPrice, LineTotal, CreatedAt)
            VALUES (@InvoiceId, @LineNumber, @Description, @Quantity, @UnitPrice, @LineTotal, @CreatedAt);
            """;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.ExecuteAsync(sql, items);
    }

    public async Task<IEnumerable<InvoiceLineItem>> GetByInvoiceIdAsync(int invoiceId)
    {
        const string sql = "SELECT * FROM InvoiceLineItems WHERE InvoiceId = @InvoiceId ORDER BY LineNumber;";
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryAsync<InvoiceLineItem>(sql, new { InvoiceId = invoiceId });
    }
}
