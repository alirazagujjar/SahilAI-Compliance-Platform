using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace SahilAI.Infrastructure.Persistence;

public sealed class DatabaseInitializer
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(string connectionString, ILogger<DatabaseInitializer> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var dbName = ExtractDatabaseName(_connectionString);
        var serverConnectionString = StripDatabase(_connectionString);

        _logger.LogInformation("Connecting to MariaDB server...");

        // Step 1: connect without a database and create it if absent
        await using (var serverConn = new MySqlConnection(serverConnectionString))
        {
            await serverConn.OpenAsync();
            await serverConn.ExecuteAsync(
                $"CREATE DATABASE IF NOT EXISTS `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            _logger.LogInformation("Database '{Db}' is ready.", dbName);
        }

        // Step 2: connect to the target database and create tables
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        _logger.LogInformation("Creating tables in '{Db}'...", dbName);

        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS Invoices (
                Id                  INT AUTO_INCREMENT PRIMARY KEY,
                InvoiceNumber       VARCHAR(100)   NOT NULL,
                VendorName          VARCHAR(255)   NOT NULL,
                VendorTrn           VARCHAR(50),
                InvoiceDate         DATE           NOT NULL,
                Subtotal            DECIMAL(18,2)  NOT NULL DEFAULT 0,
                TaxAmount           DECIMAL(18,2)  NOT NULL DEFAULT 0,
                GrandTotal          DECIMAL(18,2)  NOT NULL DEFAULT 0,
                TaxRate             DECIMAL(8,4)   NOT NULL DEFAULT 0,
                Currency            VARCHAR(10)    NOT NULL DEFAULT 'AED',
                Status              VARCHAR(30)    NOT NULL DEFAULT 'PENDING',
                Region              VARCHAR(10)    NOT NULL DEFAULT 'UAE',
                ZatcaUuid           VARCHAR(40),
                PreviousInvoiceHash VARCHAR(256),
                XmlPath             VARCHAR(512),
                XmlHash             VARCHAR(64),
                QrCodeBase64        TEXT,
                ConfidenceScore     DECIMAL(4,2)   NOT NULL DEFAULT 0,
                DocumentLanguage    VARCHAR(20)    NOT NULL DEFAULT 'EN',
                FileType            VARCHAR(20)    NOT NULL DEFAULT 'TXT',
                AnomalyFlags        JSON,
                ReasoningLog        JSON           NOT NULL DEFAULT ('{}'),
                SourceFile          VARCHAR(500),
                CreatedAt           DATETIME       NOT NULL DEFAULT CURRENT_TIMESTAMP,
                IsApproved          TINYINT(1)     NOT NULL DEFAULT 0,
                ApprovedAt          DATETIME,
                ApprovedBy          VARCHAR(100),
                UNIQUE KEY uq_invoice_number (InvoiceNumber, Region)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS AgentAuditLogs (
                Id           INT AUTO_INCREMENT PRIMARY KEY,
                AgentName    VARCHAR(100) NOT NULL,
                Action       VARCHAR(50)  NOT NULL,
                InputSummary TEXT,
                OutputJson   JSON         NOT NULL DEFAULT ('{}'),
                Status       VARCHAR(30)  NOT NULL,
                ErrorMessage TEXT,
                DurationMs   BIGINT       NOT NULL DEFAULT 0,
                InvoiceId    INT,
                CreatedAt    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT fk_audit_invoice FOREIGN KEY (InvoiceId) REFERENCES Invoices(Id) ON DELETE SET NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS InvoiceLineItems (
                Id          INT AUTO_INCREMENT PRIMARY KEY,
                InvoiceId   INT            NOT NULL,
                LineNumber  SMALLINT       NOT NULL DEFAULT 1,
                Description VARCHAR(500)   NOT NULL,
                Quantity    DECIMAL(18,4)  NOT NULL DEFAULT 1,
                UnitPrice   DECIMAL(18,2)  NOT NULL DEFAULT 0,
                LineTotal   DECIMAL(18,2)  NOT NULL DEFAULT 0,
                CreatedAt   DATETIME       NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT fk_lineitems_invoice FOREIGN KEY (InvoiceId) REFERENCES Invoices(Id) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS Vendors (
                Id           INT AUTO_INCREMENT PRIMARY KEY,
                Name         VARCHAR(255)  NOT NULL,
                TaxRegNumber VARCHAR(50),
                Region       VARCHAR(10)   NOT NULL DEFAULT 'UAE',
                IsVerified   TINYINT(1)    NOT NULL DEFAULT 0,
                Email        VARCHAR(255),
                Phone        VARCHAR(50),
                Address      TEXT,
                CreatedAt    DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt    DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                UNIQUE KEY uq_vendor_trn (TaxRegNumber, Region)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS ProcessingQueue (
                Id          INT AUTO_INCREMENT PRIMARY KEY,
                FileName    VARCHAR(500)  NOT NULL,
                FilePath    VARCHAR(1000) NOT NULL,
                FileHash    VARCHAR(64),
                Region      VARCHAR(10)   NOT NULL DEFAULT 'UAE',
                FileType    VARCHAR(20)   NOT NULL DEFAULT 'TXT',
                QueueStatus VARCHAR(30)   NOT NULL DEFAULT 'QUEUED',
                RetryCount  SMALLINT      NOT NULL DEFAULT 0,
                InvoiceId   INT,
                ErrorDetail TEXT,
                QueuedAt    DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
                ProcessedAt DATETIME,
                CONSTRAINT fk_queue_invoice FOREIGN KEY (InvoiceId) REFERENCES Invoices(Id) ON DELETE SET NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS ValidationRules (
                Id              INT AUTO_INCREMENT PRIMARY KEY,
                Region          VARCHAR(10)    NOT NULL,
                RuleCode        VARCHAR(50)    NOT NULL,
                RuleName        VARCHAR(255)   NOT NULL,
                Description     TEXT,
                IsActive        TINYINT(1)     NOT NULL DEFAULT 1,
                Severity        VARCHAR(20)    NOT NULL DEFAULT 'ERROR',
                CountryCode     CHAR(2),
                ExpectedTaxRate DECIMAL(5,2),
                MandatoryTRN    TINYINT(1)     NOT NULL DEFAULT 1,
                CreatedAt       DATETIME       NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE KEY uq_rule (Region, RuleCode)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS SystemSettings (
                SettingKey   VARCHAR(100) NOT NULL PRIMARY KEY,
                SettingValue TEXT         NOT NULL,
                Description  VARCHAR(500),
                UpdatedAt    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        await MigrateAsync(conn);
        await SeedDataAsync(conn);

        _logger.LogInformation("Schema ready — all tables created/migrated and seeded.");
    }

    private static async Task MigrateAsync(MySqlConnection conn)
    {
        // Additive migrations — safe to run on existing databases.
        // MariaDB supports IF NOT EXISTS on ALTER TABLE ADD COLUMN.

        // v1.1: FileType tracking
        await conn.ExecuteAsync("""
            ALTER TABLE Invoices
                ADD COLUMN IF NOT EXISTS FileType VARCHAR(20) NOT NULL DEFAULT 'TXT'
                    AFTER DocumentLanguage;
            """);

        await conn.ExecuteAsync("""
            ALTER TABLE ProcessingQueue
                ADD COLUMN IF NOT EXISTS FileType VARCHAR(20) NOT NULL DEFAULT 'TXT'
                    AFTER Region;
            """);

        // v1.2: HITL approval
        await conn.ExecuteAsync("""
            ALTER TABLE Invoices
                ADD COLUMN IF NOT EXISTS IsApproved  TINYINT(1) NOT NULL DEFAULT 0 AFTER CreatedAt,
                ADD COLUMN IF NOT EXISTS ApprovedAt  DATETIME            NULL       AFTER IsApproved,
                ADD COLUMN IF NOT EXISTS ApprovedBy  VARCHAR(100)        NULL       AFTER ApprovedAt;
            """);

        // v1.3: Compliance fields — ZATCA XML, QR, PIH hash chain, ValidationRules regional config
        await conn.ExecuteAsync("""
            ALTER TABLE Invoices
                ADD COLUMN IF NOT EXISTS XmlPath         VARCHAR(512) NULL AFTER PreviousInvoiceHash,
                ADD COLUMN IF NOT EXISTS XmlHash         VARCHAR(64)  NULL AFTER XmlPath,
                ADD COLUMN IF NOT EXISTS QrCodeBase64    TEXT         NULL AFTER XmlHash;
            """);

        await conn.ExecuteAsync("""
            ALTER TABLE ValidationRules
                ADD COLUMN IF NOT EXISTS CountryCode     CHAR(2)       NULL    AFTER Severity,
                ADD COLUMN IF NOT EXISTS ExpectedTaxRate DECIMAL(5,2)  NULL    AFTER CountryCode,
                ADD COLUMN IF NOT EXISTS MandatoryTRN    TINYINT(1)    NOT NULL DEFAULT 1 AFTER ExpectedTaxRate;
            """);
    }

    private static async Task SeedDataAsync(MySqlConnection conn)
    {
        await conn.ExecuteAsync("""
            INSERT IGNORE INTO ValidationRules
                (Region, RuleCode, RuleName, Severity, CountryCode, ExpectedTaxRate, MandatoryTRN)
            VALUES
                ('UAE', 'TAX_MISMATCH_CHECK',            'Tax Amount vs Subtotal x Rate',               'ERROR',   'AE', 5.00,  1),
                ('UAE', 'TOTAL_MISMATCH_CHECK',           'Grand Total vs Subtotal + Tax',               'ERROR',   'AE', 5.00,  1),
                ('UAE', 'MISSING_TRN',                    'Vendor TRN is required',                      'ERROR',   'AE', 5.00,  1),
                ('UAE', 'INVALID_TRN_FORMAT',             'UAE TRN must be 15 digits',                   'ERROR',   'AE', 5.00,  1),
                ('SA',  'TAX_MISMATCH_CHECK',             'Tax Amount vs Subtotal x Rate',               'ERROR',   'SA', 15.00, 1),
                ('SA',  'MISSING_TRN',                    'Vendor TRN is required',                      'ERROR',   'SA', 15.00, 1),
                ('SA',  'INVALID_ZATCA_TRN',              'ZATCA TRN must be 15 digits',                 'ERROR',   'SA', 15.00, 1),
                ('SA',  'ZATCA_UUID_REQUIRED',            'ZATCA Phase 2 UUID must exist',               'WARNING', 'SA', 15.00, 1),
                ('USA', 'MISSING_INVOICE_NUMBER',         'Invoice number is blank',                     'ERROR',   'US', 0.00,  0),
                ('ALL', 'DUPLICATE_INVOICE',              'Invoice number already exists',               'ERROR',   NULL, NULL,  1),
                ('ALL', 'MISSING_INVOICE_NUMBER',         'Invoice number is blank',                     'ERROR',   NULL, NULL,  1),
                ('ALL', 'LINE_ITEM_SUBTOTAL_MISMATCH',    'Sum of line items does not match subtotal',   'ERROR',   NULL, NULL,  1),
                ('ALL', 'HITL_REQUIRED',                  'Low-confidence invoice requires human review','WARNING', NULL, NULL,  1);
            """);

        await conn.ExecuteAsync("""
            INSERT IGNORE INTO SystemSettings (SettingKey, SettingValue, Description) VALUES
                ('UAE_TAX_RATE',     '0.05',  'Standard UAE VAT rate (5%)'),
                ('SA_TAX_RATE',      '0.15',  'Standard Saudi VAT rate (15%)'),
                ('USA_TAX_RATE',     '0.00',  'US invoices - no federal VAT'),
                ('MAX_RETRY_COUNT',  '3',     'Max retry attempts for failed queue items'),
                ('DEFAULT_CURRENCY', 'AED',   'Default currency if not detected'),
                ('DEFAULT_REGION',   'UAE',   'Default compliance region');
            """);
    }

    // Parses the Database= value from the connection string using MySqlConnectionStringBuilder.
    private static string ExtractDatabaseName(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        var db = builder.Database;
        if (string.IsNullOrWhiteSpace(db))
            throw new InvalidOperationException("Connection string must include a Database= value.");
        return db;
    }

    // Returns a connection string with the Database key removed so we can connect at the server level.
    private static string StripDatabase(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString) { Database = string.Empty };
        return builder.ConnectionString;
    }
}
