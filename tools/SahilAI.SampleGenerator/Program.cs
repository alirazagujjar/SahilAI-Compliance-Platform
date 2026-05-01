using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// Resolve output path: two levels up from the tool folder → project root → samples/
var root    = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var samples = Path.Combine(root, "samples");
Directory.CreateDirectory(samples);

Console.WriteLine($"Writing samples to: {samples}");

GeneratePdf(Path.Combine(samples, "sample_invoice.pdf"));
GeneratePng(Path.Combine(samples, "sample_invoice.png"));

Console.WriteLine("Done. Two sample files created in samples/");

// ──────────────────────────────────────────────────────────────────────────────
// PDF — text-selectable invoice using PdfSharpCore
// ──────────────────────────────────────────────────────────────────────────────
static void GeneratePdf(string path)
{
    using var document = new PdfDocument();
    document.Info.Title   = "Sample Invoice — SahilAI Test";
    document.Info.Author  = "SahilAI Sample Generator";

    var page     = document.AddPage();
    page.Width   = XUnit.FromPoint(595);   // A4
    page.Height  = XUnit.FromPoint(842);

    using var gfx = XGraphics.FromPdfPage(page);

    var fontBold    = new XFont("Arial", 18, XFontStyle.Bold);
    var fontTitle   = new XFont("Arial", 13, XFontStyle.Bold);
    var fontNormal  = new XFont("Arial", 11, XFontStyle.Regular);
    var fontSmall   = new XFont("Arial",  9, XFontStyle.Regular);
    var black  = XBrushes.Black;
    var grey   = new XSolidBrush(XColor.FromArgb(100, 100, 100));
    var blue   = new XSolidBrush(XColor.FromArgb(0,   82,  165));

    double margin = 50;
    double w      = page.Width.Point - margin * 2;
    double y      = margin;

    // Header bar
    gfx.DrawRectangle(blue, margin - 10, y - 10, w + 20, 45);
    gfx.DrawString("GULF TECH SOLUTIONS FZE", fontBold, XBrushes.White,
        new XRect(margin, y, w, 35), XStringFormats.CenterLeft);
    gfx.DrawString("TAX INVOICE", fontTitle, XBrushes.White,
        new XRect(margin, y, w, 35), XStringFormats.CenterRight);
    y += 55;

    // Vendor info
    gfx.DrawString("TRN: 100234567890123", fontNormal, grey, margin, y); y += 18;
    gfx.DrawString("P.O. Box 12345, Dubai Internet City, Dubai, UAE", fontNormal, grey, margin, y); y += 18;
    gfx.DrawString("Tel: +971 4 555 0100   |   Email: accounts@gulftechfze.ae", fontNormal, grey, margin, y); y += 30;

    // Divider
    gfx.DrawLine(XPens.LightGray, margin, y, margin + w, y); y += 15;

    // Invoice meta
    double col2 = margin + w / 2;
    gfx.DrawString("Invoice Number:", fontTitle,  black, margin, y);
    gfx.DrawString("INV-2024-00452",  fontNormal, black, margin + 130, y);
    gfx.DrawString("Invoice Date:",   fontTitle,  black, col2,  y);
    gfx.DrawString("15 Oct 2024",     fontNormal, black, col2 + 110,  y);
    y += 20;

    gfx.DrawString("Due Date:",       fontTitle,  black, margin, y);
    gfx.DrawString("14 Nov 2024",     fontNormal, black, margin + 130, y);
    gfx.DrawString("Currency:",       fontTitle,  black, col2,  y);
    gfx.DrawString("AED",             fontNormal, black, col2 + 110,  y);
    y += 30;

    // Bill to
    gfx.DrawString("Bill To:", fontTitle, black, margin, y); y += 18;
    gfx.DrawString("Emirates Shipping & Logistics LLC", fontNormal, black, margin, y); y += 16;
    gfx.DrawString("TRN: 200345678901234", fontNormal, grey, margin, y); y += 16;
    gfx.DrawString("Jebel Ali Free Zone, Dubai, UAE",  fontNormal, grey, margin, y); y += 25;

    gfx.DrawLine(XPens.LightGray, margin, y, margin + w, y); y += 12;

    // Line-items table header
    double[] cols  = { margin, margin + 250, margin + 340, margin + 420, margin + 500 };
    string[] heads = { "Description", "Qty", "Unit Price", "VAT 5%", "Total" };
    for (int i = 0; i < heads.Length; i++)
        gfx.DrawString(heads[i], fontTitle, black, cols[i], y);
    y += 5;
    gfx.DrawLine(new XPen(XColors.DimGray, 0.5), margin, y, margin + w, y); y += 10;

    // Line items
    var items = new[]
    {
        ("Cloud Infrastructure Setup (3 months)",       1m,  4500.00m),
        ("Managed Firewall & Security Services",        1m,  1800.00m),
        ("On-site Engineer Support (8 hrs × AED 250)", 8m,   250.00m),
        ("Software Licensing — Annual Subscription",    1m,  2200.00m),
    };

    decimal subtotal = 0;
    foreach (var (desc, qty, unitPrice) in items)
    {
        decimal lineTotal = qty * unitPrice;
        decimal vat       = Math.Round(lineTotal * 0.05m, 2);
        subtotal += lineTotal;

        gfx.DrawString(desc,                   fontSmall, black, cols[0], y);
        gfx.DrawString(qty.ToString("0.##"),   fontSmall, black, cols[1], y);
        gfx.DrawString($"AED {unitPrice:N2}",  fontSmall, black, cols[2], y);
        gfx.DrawString($"AED {vat:N2}",        fontSmall, black, cols[3], y);
        gfx.DrawString($"AED {lineTotal:N2}",  fontSmall, black, cols[4], y);
        y += 18;
    }

    gfx.DrawLine(new XPen(XColors.DimGray, 0.5), margin, y, margin + w, y); y += 12;

    // Totals
    decimal taxRate   = 0.05m;
    decimal taxAmount = Math.Round(subtotal * taxRate, 2);
    decimal grand     = subtotal + taxAmount;

    void TotalRow(string label, decimal amount, XFont font)
    {
        gfx.DrawString(label,                font, black, cols[3], y);
        gfx.DrawString($"AED {amount:N2}",   font, black, cols[4], y);
        y += 18;
    }

    TotalRow("Subtotal:",  subtotal,   fontNormal);
    TotalRow("VAT (5%):", taxAmount,   fontNormal);
    gfx.DrawLine(XPens.DimGray, cols[3], y - 2, margin + w, y - 2);
    TotalRow("TOTAL DUE:", grand,      fontBold);
    y += 10;

    // Notes
    gfx.DrawLine(XPens.LightGray, margin, y, margin + w, y); y += 12;
    gfx.DrawString("Payment Terms: Net 30 days", fontSmall, grey, margin, y); y += 14;
    gfx.DrawString("Bank: Emirates NBD  |  IBAN: AE070260001015504250000  |  SWIFT: EBILAEAD", fontSmall, grey, margin, y); y += 14;
    gfx.DrawString("This is a computer-generated invoice and does not require a physical signature.", fontSmall, grey, margin, y);

    document.Save(path);
    Console.WriteLine($"  [PDF] {path}");
}

// ──────────────────────────────────────────────────────────────────────────────
// PNG — rasterised invoice scan simulation using ImageSharp
// ──────────────────────────────────────────────────────────────────────────────
static void GeneratePng(string path)
{
    const int W = 900, H = 1200;

    using var image = new Image<Rgba32>(W, H);

    // Load system font (Arial or fallback)
    var collection = new FontCollection();
    FontFamily family;
    try   { family = SystemFonts.Get("Arial"); }
    catch { family = SystemFonts.Families.First(); }

    var fontH1   = family.CreateFont(22, FontStyle.Bold);
    var fontH2   = family.CreateFont(14, FontStyle.Bold);
    var fontBody = family.CreateFont(12, FontStyle.Regular);
    var fontSm   = family.CreateFont(10, FontStyle.Regular);

    var white   = Color.White;
    var navy    = Color.FromRgb(0, 51, 102);
    var black   = Color.Black;
    var darkGrey= Color.FromRgb(80, 80, 80);

    image.Mutate(ctx =>
    {
        ctx.Fill(white);

        // Header band
        ctx.Fill(navy, new RectangleF(0, 0, W, 70));
        ctx.DrawText("ARABIAN CARGO SERVICES LLC", fontH1, Color.White, new PointF(30, 12));
        ctx.DrawText("TAX INVOICE", fontH2, Color.White, new PointF(680, 22));

        float y = 90;

        // Vendor details
        ctx.DrawText("TRN: 300456789012345", fontBody, darkGrey, new PointF(30, y)); y += 20;
        ctx.DrawText("Sharjah Airport International Free Zone, Sharjah, UAE", fontBody, darkGrey, new PointF(30, y)); y += 20;
        ctx.DrawText("Tel: +971 6 555 0200  |  Email: billing@arabiancargo.ae", fontBody, darkGrey, new PointF(30, y)); y += 35;

        // Divider
        ctx.DrawLine(Color.LightGray, 1.5f, new PointF(30, y), new PointF(W - 30, y)); y += 15;

        // Invoice meta
        ctx.DrawText("Invoice No.:", fontH2, black, new PointF(30, y));
        ctx.DrawText("ACS-2024-00318", fontBody, black, new PointF(145, y));
        ctx.DrawText("Date:", fontH2, black, new PointF(520, y));
        ctx.DrawText("20 Nov 2024", fontBody, black, new PointF(570, y)); y += 22;

        ctx.DrawText("Due Date:", fontH2, black, new PointF(30, y));
        ctx.DrawText("20 Dec 2024", fontBody, black, new PointF(145, y));
        ctx.DrawText("Currency:", fontH2, black, new PointF(520, y));
        ctx.DrawText("AED", fontBody, black, new PointF(608, y)); y += 30;

        // Bill To
        ctx.DrawText("Bill To:", fontH2, black, new PointF(30, y)); y += 18;
        ctx.DrawText("Falcon Trade & Investment Group", fontBody, black, new PointF(30, y)); y += 18;
        ctx.DrawText("TRN: 400567890123456", fontBody, darkGrey, new PointF(30, y)); y += 18;
        ctx.DrawText("Abu Dhabi Global Market, Abu Dhabi, UAE", fontBody, darkGrey, new PointF(30, y)); y += 30;

        ctx.DrawLine(Color.LightGray, 1f, new PointF(30, y), new PointF(W - 30, y)); y += 12;

        // Table header
        float[] cx = { 30, 460, 560, 660, 770 };
        ctx.DrawText("Description",  fontH2, black, new PointF(cx[0], y));
        ctx.DrawText("Qty",          fontH2, black, new PointF(cx[1], y));
        ctx.DrawText("Unit",         fontH2, black, new PointF(cx[2], y));
        ctx.DrawText("VAT",          fontH2, black, new PointF(cx[3], y));
        ctx.DrawText("Total",        fontH2, black, new PointF(cx[4], y));
        y += 8;
        ctx.DrawLine(Color.DimGray, 1f, new PointF(30, y), new PointF(W - 30, y)); y += 10;

        // Line items
        var rows = new[]
        {
            ("Air Freight — DXB to LHR (500 kg @ 12 AED/kg)", 1m, 6000.00m),
            ("Custom Clearance & Documentation",               1m,  850.00m),
            ("Warehouse Storage (7 days × AED 120/day)",       7m,  120.00m),
            ("Packaging & Crating Services",                   1m,  450.00m),
            ("Insurance — Cargo (0.5% of declared value)",     1m,  375.00m),
        };

        decimal sub = 0;
        foreach (var (desc, qty, up) in rows)
        {
            decimal lt  = qty * up;
            decimal vat = Math.Round(lt * 0.05m, 2);
            sub += lt;
            ctx.DrawText(desc,                 fontSm,   black, new PointF(cx[0], y));
            ctx.DrawText(qty.ToString("0.##"), fontSm,   black, new PointF(cx[1], y));
            ctx.DrawText($"{up:N2}",           fontSm,   black, new PointF(cx[2], y));
            ctx.DrawText($"{vat:N2}",          fontSm,   black, new PointF(cx[3], y));
            ctx.DrawText($"{lt:N2}",           fontSm,   black, new PointF(cx[4], y));
            y += 20;
        }

        y += 5;
        ctx.DrawLine(Color.DimGray, 1f, new PointF(30, y), new PointF(W - 30, y)); y += 12;

        decimal tax   = Math.Round(sub * 0.05m, 2);
        decimal grand = sub + tax;

        ctx.DrawText("Subtotal:",  fontH2,   black, new PointF(cx[3] - 90, y));
        ctx.DrawText($"AED {sub:N2}",  fontBody, black, new PointF(cx[4], y)); y += 22;
        ctx.DrawText("VAT (5%):", fontH2,   black, new PointF(cx[3] - 90, y));
        ctx.DrawText($"AED {tax:N2}",  fontBody, black, new PointF(cx[4], y)); y += 22;
        ctx.DrawLine(Color.DimGray, 1.5f, new PointF(cx[3] - 100, y), new PointF(W - 30, y)); y += 6;
        ctx.DrawText("TOTAL DUE:", fontH2, navy,  new PointF(cx[3] - 90, y));
        ctx.DrawText($"AED {grand:N2}", fontH2, navy, new PointF(cx[4], y)); y += 35;

        ctx.DrawLine(Color.LightGray, 1f, new PointF(30, y), new PointF(W - 30, y)); y += 14;
        ctx.DrawText("Payment: Bank Transfer within 30 days", fontSm, darkGrey, new PointF(30, y)); y += 16;
        ctx.DrawText("Bank: First Abu Dhabi Bank  |  IBAN: AE140351234567890123456  |  SWIFT: NBADAEAA", fontSm, darkGrey, new PointF(30, y)); y += 16;
        ctx.DrawText("This invoice was generated electronically and is valid without a signature.", fontSm, darkGrey, new PointF(30, y));
    });

    image.SaveAsPng(path);
    Console.WriteLine($"  [PNG] {path}");
}
