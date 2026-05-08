using Microsoft.AspNetCore.SignalR;

namespace SahilAI.Web.Hubs;

/// <summary>
/// SignalR hub that external clients (future mobile / API consumers) can connect
/// to for real-time invoice events. Blazor Server components use LiveCountService
/// directly (in-process C# events) — no JS round-trip needed on the server side.
///
/// Groups:
///   "dashboard" — receives "countsUpdated" (Dictionary&lt;string,int&gt;)
///   "review"    — receives "newInvoice"    ({ Id, InvoiceNumber, VendorName, ... })
/// </summary>
public sealed class InvoiceHub : Hub
{
    public Task SubscribeDashboard()
        => Groups.AddToGroupAsync(Context.ConnectionId, "dashboard");

    public Task UnsubscribeDashboard()
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, "dashboard");

    public Task SubscribeReview()
        => Groups.AddToGroupAsync(Context.ConnectionId, "review");

    public Task UnsubscribeReview()
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, "review");
}
