using Dapper;
using MySqlConnector;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Infrastructure.Persistence;

public sealed class MariaDbAuditLogRepository : IAuditLogRepository
{
    private readonly string _connectionString;

    public MariaDbAuditLogRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InsertAsync(AgentAuditLog log)
    {
        const string sql = """
            INSERT INTO AgentAuditLogs
                (AgentName, Action, InputSummary, OutputJson, Status, ErrorMessage, DurationMs, InvoiceId, CreatedAt)
            VALUES
                (@AgentName, @Action, @InputSummary, @OutputJson, @Status, @ErrorMessage, @DurationMs, @InvoiceId, @CreatedAt);
            """;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.ExecuteAsync(sql, log);
    }

    public async Task<IEnumerable<AgentAuditLog>> GetByInvoiceIdAsync(int invoiceId)
    {
        const string sql = "SELECT * FROM AgentAuditLogs WHERE InvoiceId = @InvoiceId ORDER BY CreatedAt;";
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryAsync<AgentAuditLog>(sql, new { InvoiceId = invoiceId });
    }
}
