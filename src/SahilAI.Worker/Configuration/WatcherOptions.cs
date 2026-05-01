namespace SahilAI.Worker.Configuration;

public sealed class WatcherOptions
{
    public string InboxPath { get; set; } = "inbox";
    public string SuccessPath { get; set; } = "processed/success";
    public string ReviewPath { get; set; } = "processed/review";
    public string InvalidFormatPath { get; set; } = "processed/invalidformat";
    public string XmlOutputPath { get; set; } = "processed/xml";
    public string DefaultRegion { get; set; } = "UAE";
    public decimal ConfidenceThreshold { get; set; } = 0.90m;
}
