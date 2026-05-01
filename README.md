# Sahil AI — Agentic RPA for Enterprise Compliance & Data Automation

> **Target Markets:** USA (Enterprise Security) · UAE (VAT/TRN) · Saudi Arabia (ZATCA Phase 2)

Sahil AI is a **.NET 9 agentic automation platform** that replaces manual invoice data entry for finance and logistics departments. It uses **Microsoft Semantic Kernel** with a local **Ollama** LLM (or Azure OpenAI in production) to extract, validate, and persist invoice data into **MariaDB** — with zero human touch on the standard flow.

---

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                   SahilAI Solution                   │
│                                                      │
│  SahilAI.Domain          ← Entities, Interfaces      │
│       ↑                                              │
│  SahilAI.Application     ← Use Cases, Validation     │
│       ↑                                              │
│  SahilAI.Infrastructure  ← SK Agent, Dapper/MariaDB  │
│       ↑                                              │
│  SahilAI.Worker          ← Console Host, FileWatcher │
└─────────────────────────────────────────────────────┘
```

**Clean Architecture** — dependencies flow inward only. Domain has no external dependencies.

---

## Tech Stack

| Layer          | Technology                               |
|----------------|------------------------------------------|
| Framework      | .NET 9.0 (C#)                            |
| AI Orchestration | Microsoft Semantic Kernel 1.30          |
| LLM (Local)    | Ollama (Llama 3 / Mistral)               |
| LLM (Production) | Azure OpenAI                           |
| Database       | MariaDB                                  |
| Data Access    | Dapper                                   |
| Logging        | Serilog (Console + Rolling File)         |
| Testing        | xUnit                                    |

---

## Features

- **Ingestion Engine** — watches a local `inbox/` folder for new `.txt` invoice files
- **Compliance Agent** — LLM-powered extraction of vendor, TRN, line items, subtotal, tax, and totals
- **Validation Rules** — flags tax mismatches, missing TRNs, invalid UAE/ZATCA TRN formats, duplicate invoices
- **Persistence** — Dapper saves invoices and a full reasoning log (JSON) to MariaDB
- **ZATCA Phase 2** — generates mock UUID and Previous-Invoice-Hash for Saudi Arabia compliance
- **Audit Trail** — every agent action is logged to `AgentAuditLogs` table with duration and status

---

## Quick Start

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [MariaDB](https://mariadb.org/download/) running on port 3306
- [Ollama](https://ollama.ai/) running on port 11434 with `llama3` pulled

```bash
ollama pull llama3
```

### 1. Configure

Edit `src/SahilAI.Worker/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "MariaDb": "Server=localhost;Port=3306;Database=sahilai;Uid=root;Pwd=YOUR_PASSWORD;"
  },
  "AI": {
    "ModelId": "llama3",
    "Endpoint": "http://localhost:11434/v1",
    "ApiKey": "ollama"
  },
  "Watcher": {
    "InboxPath": "inbox",
    "DefaultRegion": "UAE"
  }
}
```

### 2. Run

```bash
cd src/SahilAI.Worker
dotnet run
```

The worker auto-creates the MariaDB schema on first run.

### 3. Process an Invoice

Drop any text file into the `inbox/` folder:

```bash
copy samples\dubai_logistics_invoice.txt src\SahilAI.Worker\inbox\
```

Watch the console output:

```
[10:15:02 INF] New file detected: dubai_logistics_invoice.txt
[10:15:04 INF] [VALID] Invoice saved with Id=1. Anomalies: 0
```

---

## Switching to Azure OpenAI (Production)

Update `appsettings.json`:

```json
"AI": {
  "ModelId": "gpt-4o",
  "Endpoint": "https://YOUR_RESOURCE.openai.azure.com/",
  "ApiKey": "YOUR_AZURE_OPENAI_KEY"
}
```

No code changes required — the kernel swap is configuration-only.

---

## Database Schema

```sql
-- Invoices: extracted and validated invoice data
CREATE TABLE Invoices (
    Id              INT AUTO_INCREMENT PRIMARY KEY,
    InvoiceNumber   VARCHAR(100),
    VendorName      VARCHAR(255),
    VendorTrn       VARCHAR(50),
    InvoiceDate     DATE,
    Subtotal        DECIMAL(18,2),
    TaxAmount       DECIMAL(18,2),
    GrandTotal      DECIMAL(18,2),
    TaxRate         DECIMAL(8,4),
    Currency        VARCHAR(10),
    Status          VARCHAR(30),     -- VALID | FLAGGED | DUPLICATE | ERROR
    Region          VARCHAR(10),     -- UAE | SA | USA
    ZatcaUuid       VARCHAR(40),     -- ZATCA Phase 2
    PreviousInvoiceHash VARCHAR(256),
    AnomalyFlags    JSON,
    ReasoningLog    JSON,            -- LLM reasoning trace
    SourceFile      VARCHAR(500),
    CreatedAt       DATETIME
);

-- AgentAuditLogs: full audit trail of every agent action
CREATE TABLE AgentAuditLogs (
    Id           INT AUTO_INCREMENT PRIMARY KEY,
    AgentName    VARCHAR(100),
    Action       VARCHAR(50),
    InputSummary TEXT,
    OutputJson   JSON,
    Status       VARCHAR(30),
    ErrorMessage TEXT,
    DurationMs   BIGINT,
    InvoiceId    INT,
    CreatedAt    DATETIME
);
```

---

## Case Study: Dubai Logistics

**Problem:** 500 invoices/day arriving by email → 4 hours/day of manual ERP data entry.

**Solution:** Sahil AI watches the inbox, extracts UAE TRN and all financial fields, validates VAT (5%), and writes directly to MariaDB.

**Result:** Manual effort reduced from **4 hours → 5 minutes** of exception review.

---

## Project Structure

```
SahilAI/
├── src/
│   ├── SahilAI.Domain/          # Entities, Interfaces (no external deps)
│   ├── SahilAI.Application/     # Business logic, DTOs, validation
│   ├── SahilAI.Infrastructure/  # SK Agent, Dapper repos, DB init
│   └── SahilAI.Worker/          # Console host, FileWatcher, config
├── tests/
│   └── SahilAI.Tests/           # xUnit unit tests (8 tests)
├── samples/
│   └── dubai_logistics_invoice.txt
└── SahilAI.sln
```

---

## Running Tests

```bash
dotnet test
```

```
Passed! - Failed: 0, Passed: 8, Skipped: 0
```

---

*Built with Microsoft Semantic Kernel · Dapper · MariaDB · Ollama*
