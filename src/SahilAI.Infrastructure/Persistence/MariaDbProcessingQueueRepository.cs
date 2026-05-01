using Dapper;
using MySqlConnector;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Infrastructure.Persistence;

public sealed class MariaDbProcessingQueueRepository : IProcessingQueueRepository
{
    private readonly string _connectionString;

    public MariaDbProcessingQueueRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<int> EnqueueAsync(ProcessingQueueItem item)
    {
        const string sql = """
            INSERT INTO ProcessingQueue (FileName, FilePath, FileHash, Region, FileType, QueueStatus, QueuedAt)
            VALUES (@FileName, @FilePath, @FileHash, @Region, @FileType, @QueueStatus, @QueuedAt);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = new MySqlConnection(_connectionString);
        return await conn.ExecuteScalarAsync<int>(sql, item);
    }

    public async Task UpdateAsync(int id, string status, int? invoiceId = null, string? error = null)
    {
        const string sql = """
            UPDATE ProcessingQueue
            SET QueueStatus = @Status,
                InvoiceId   = @InvoiceId,
                ErrorDetail = @Error,
                ProcessedAt = CASE WHEN @Status IN ('DONE','FAILED') THEN NOW() ELSE NULL END
            WHERE Id = @Id;
            """;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.ExecuteAsync(sql, new { Id = id, Status = status, InvoiceId = invoiceId, Error = error });
    }
}
