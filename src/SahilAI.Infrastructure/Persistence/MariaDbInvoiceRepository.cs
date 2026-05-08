using Dapper;
using MySqlConnector;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Infrastructure.Persistence;

public sealed class MariaDbInvoiceRepository : IInvoiceRepository
{
    private readonly string _connectionString;

    public MariaDbInvoiceRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<int> InsertAsync(Invoice invoice)
    {
        const string sql = """
            INSERT INTO Invoices
                (InvoiceNumber, VendorName, VendorTrn, InvoiceDate, Subtotal, TaxAmount,
                 GrandTotal, TaxRate, Currency, Status, Region, ConfidenceScore,
                 DocumentLanguage, FileType, ZatcaUuid, PreviousInvoiceHash,
                 XmlPath, QrCodeBase64, AnomalyFlags,
                 ReasoningLog, SourceFile, CreatedAt, IsApproved)
            VALUES
                (@InvoiceNumber, @VendorName, @VendorTrn, @InvoiceDate, @Subtotal, @TaxAmount,
                 @GrandTotal, @TaxRate, @Currency, @Status, @Region, @ConfidenceScore,
                 @DocumentLanguage, @FileType, @ZatcaUuid, @PreviousInvoiceHash,
                 @XmlPath, @QrCodeBase64, @AnomalyFlags,
                 @ReasoningLog, @SourceFile, @CreatedAt, @IsApproved);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = new MySqlConnection(_connectionString);
        return await conn.ExecuteScalarAsync<int>(sql, invoice);
    }

    public async Task<Invoice?> GetByInvoiceNumberAsync(string invoiceNumber)
    {
        const string sql = "SELECT * FROM Invoices WHERE InvoiceNumber = @InvoiceNumber LIMIT 1;";
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryFirstOrDefaultAsync<Invoice>(sql, new { InvoiceNumber = invoiceNumber });
    }

    public async Task<IEnumerable<Invoice>> GetFlaggedAsync()
    {
        const string sql = "SELECT * FROM Invoices WHERE Status IN ('FLAGGED', 'DUPLICATE') ORDER BY CreatedAt DESC;";
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryAsync<Invoice>(sql);
    }

    public async Task UpdateStatusAsync(int id, string status)
    {
        const string sql = "UPDATE Invoices SET Status = @Status WHERE Id = @Id;";
        await using var conn = new MySqlConnection(_connectionString);
        await conn.ExecuteAsync(sql, new { Id = id, Status = status });
    }

    public async Task<IEnumerable<Invoice>> GetPendingApprovalAsync()
    {
        const string sql = """
            SELECT * FROM Invoices
            WHERE IsApproved = 1
              AND Status NOT IN ('APPROVED', 'DUPLICATE', 'ERROR')
            ORDER BY ApprovedAt ASC;
            """;
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryAsync<Invoice>(sql);
    }

    public async Task MarkApprovedAsync(int id, string processedBy)
    {
        const string sql = """
            UPDATE Invoices
            SET Status = 'APPROVED', ApprovedBy = @ProcessedBy
            WHERE Id = @Id;
            """;
        await using var conn = new MySqlConnection(_connectionString);
        await conn.ExecuteAsync(sql, new { Id = id, ProcessedBy = processedBy });
    }

    public async Task<string?> GetPreviousXmlHashAsync(string vendorTrn, int excludeInvoiceId)
    {
        const string sql = """
            SELECT XmlHash FROM Invoices
            WHERE VendorTrn = @VendorTrn
              AND Id        < @ExcludeId
              AND Status   IN ('VALID', 'APPROVED')
              AND XmlHash  IS NOT NULL
            ORDER BY Id DESC
            LIMIT 1;
            """;
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.ExecuteScalarAsync<string?>(sql,
            new { VendorTrn = vendorTrn, ExcludeId = excludeInvoiceId });
    }

    public async Task UpdateComplianceFieldsAsync(
        int id, string? xmlPath, string? xmlHash, string? qrCode, string? previousHash)
    {
        const string sql = """
            UPDATE Invoices
            SET XmlPath             = @XmlPath,
                XmlHash             = @XmlHash,
                QrCodeBase64        = @QrCode,
                PreviousInvoiceHash = @PreviousHash
            WHERE Id = @Id;
            """;
        await using var conn = new MySqlConnection(_connectionString);
        await conn.ExecuteAsync(sql, new { Id = id, XmlPath = xmlPath, XmlHash = xmlHash,
                                           QrCode = qrCode, PreviousHash = previousHash });
    }

    public async Task<Invoice?> GetByIdAsync(int id)
    {
        const string sql = "SELECT * FROM Invoices WHERE Id = @Id LIMIT 1;";
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryFirstOrDefaultAsync<Invoice>(sql, new { Id = id });
    }

    public async Task<IEnumerable<Invoice>> GetReviewQueueAsync()
    {
        const string sql = """
            SELECT * FROM Invoices
            WHERE Status = 'REVIEW'
            ORDER BY CreatedAt DESC;
            """;
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryAsync<Invoice>(sql);
    }

    public async Task RecordApprovalMetadataAsync(int id, string approvedBy)
    {
        const string sql = """
            UPDATE Invoices
            SET IsApproved = 1,
                ApprovedAt = UTC_TIMESTAMP(),
                ApprovedBy = @ApprovedBy
            WHERE Id = @Id;
            """;
        await using var conn = new MySqlConnection(_connectionString);
        await conn.ExecuteAsync(sql, new { Id = id, ApprovedBy = approvedBy });
    }

    public async Task<IReadOnlyDictionary<string, int>> GetStatusCountsAsync()
    {
        const string sql = "SELECT Status, COUNT(*) AS Cnt FROM Invoices GROUP BY Status;";
        await using var conn = new MySqlConnection(_connectionString);
        var rows = await conn.QueryAsync<(string Status, int Cnt)>(sql);
        return rows.ToDictionary(r => r.Status, r => r.Cnt);
    }

    public async Task<IEnumerable<Invoice>> GetProcessedAsync()
    {
        const string sql = """
            SELECT * FROM Invoices
            WHERE Status IN ('VALID', 'APPROVED')
            ORDER BY CreatedAt DESC
            LIMIT 200;
            """;
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryAsync<Invoice>(sql);
    }

    public async Task<IEnumerable<Invoice>> GetNotSupportedAsync()
    {
        const string sql = """
            SELECT * FROM Invoices
            WHERE Status = 'NOT_SUPPORTED_FORMAT'
            ORDER BY CreatedAt DESC
            LIMIT 100;
            """;
        await using var conn = new MySqlConnection(_connectionString);
        return await conn.QueryAsync<Invoice>(sql);
    }

    public async Task UpdateCoreFieldsAsync(
        int id, string invoiceNumber, string vendorName, string vendorTrn,
        DateTime invoiceDate, decimal subtotal, decimal taxAmount, decimal grandTotal,
        decimal taxRate, string currency)
    {
        const string sql = """
            UPDATE Invoices
            SET InvoiceNumber = @InvoiceNumber,
                VendorName    = @VendorName,
                VendorTrn     = @VendorTrn,
                InvoiceDate   = @InvoiceDate,
                Subtotal      = @Subtotal,
                TaxAmount     = @TaxAmount,
                GrandTotal    = @GrandTotal,
                TaxRate       = @TaxRate,
                Currency      = @Currency
            WHERE Id = @Id;
            """;
        await using var conn = new MySqlConnection(_connectionString);
        await conn.ExecuteAsync(sql, new
        {
            Id = id, InvoiceNumber = invoiceNumber, VendorName = vendorName,
            VendorTrn = vendorTrn, InvoiceDate = invoiceDate, Subtotal = subtotal,
            TaxAmount = taxAmount, GrandTotal = grandTotal, TaxRate = taxRate,
            Currency = currency
        });
    }
}
