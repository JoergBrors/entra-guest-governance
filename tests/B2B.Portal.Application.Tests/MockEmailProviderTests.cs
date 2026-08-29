using B2B.Portal.Application.Ports;
using B2B.Portal.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace B2B.Portal.Application.Tests;

/// <summary>
/// Notification-Mock-Test (MVP-Dokument, TESTS / QUALITY GATES). Der Mock muss Sender,
/// Recipient, Template, CorrelationId und Workload-Kontext nachvollziehbar protokollieren
/// (siehe MVP-Verification-Prompt, Punkt 10) statt tatsächlich zu senden.
/// </summary>
public class MockEmailProviderTests
{
    [Fact]
    public async Task SendAsync_RecordsPreviewInSink_WithFullContext()
    {
        var provider = new MockEmailProvider(NullLogger<MockEmailProvider>.Instance);
        var correlationId = Guid.NewGuid();

        var message = new EmailMessage(
            SenderMailbox: "b2b-notifications@contoso.example",
            RecipientMail: "anna@contoso.example",
            TemplateId: "invitation-confirmed",
            TemplateData: new Dictionary<string, string> { ["GuestName"] = "Anna" },
            CorrelationId: correlationId,
            WorkloadContext: "SAP S/4 Projekt");

        await provider.SendAsync(message, CancellationToken.None);

        var recorded = Assert.Single(provider.Sink);
        Assert.Equal("b2b-notifications@contoso.example", recorded.SenderMailbox);
        Assert.Equal("anna@contoso.example", recorded.RecipientMail);
        Assert.Equal("invitation-confirmed", recorded.TemplateId);
        Assert.Equal(correlationId, recorded.CorrelationId);
        Assert.Equal("SAP S/4 Projekt", recorded.WorkloadContext);
    }

    [Fact]
    public async Task GraphSharedMailboxEmailProvider_WithGraphWritesDisabled_Throws()
    {
        IEmailProvider provider = new GraphSharedMailboxEmailProvider(
            sharedMailbox: "b2b-notifications@contoso.example", allowGraphWrites: false);

        var message = new EmailMessage(
            "b2b-notifications@contoso.example", "anna@contoso.example", "template",
            new Dictionary<string, string>(), Guid.NewGuid(), null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SendAsync(message, CancellationToken.None));
    }
}
