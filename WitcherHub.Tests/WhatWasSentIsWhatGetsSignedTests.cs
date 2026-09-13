using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Services.Contracts.Delivery;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Services.Contracts.Delivery;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

/// <summary>
/// The customer signs the document that was sent to them.
///
/// The signing page used to render the contract's current wording, and — when
/// that was empty — rebuild it from whichever draft happened to be approved,
/// writing the result back to the database from an anonymous public page. So a
/// link sent on Monday showed Tuesday's wording, with nothing to say it had
/// changed, and opening a link could alter the contract.
///
/// A signature obtained that way is a signature on a document nobody sent. The
/// wording is frozen onto the request when the link is issued, and these are
/// about that: what is frozen, what may be frozen at all, and what happens to
/// the link when the contract moves on.
///
/// Runs against a real PostgreSQL database when one is reachable and skips when
/// it is not. Override the connection with WITCHERHUB_TEST_DB.
/// </summary>
public class WhatWasSentIsWhatGetsSignedTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whfirst;Username=postgres";

    private const string PublicUrl = "https://app.netwitcher.de";

    private AppDbContext? _db;
    private Guid _projectId;
    private Guid _customerId;

    private bool Available => _db is not null;

    public async Task InitializeAsync()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("WITCHERHUB_TEST_DB") ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        var db = new AppDbContext(options);

        try
        {
            await db.Database.EnsureCreatedAsync();
        }
        catch
        {
            await db.DisposeAsync();
            return;
        }

        _db = db;

        var customer = new Customer { Id = Guid.NewGuid(), Name = "Musterfirma GmbH", TaxId = "DE123456789" };
        _customerId = customer.Id;

        db.Add(customer);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Title = "Online Verkauf Verwaltung",
            CustomerId = customer.Id,
            Description = "Laufende Betreuung."
        };

        db.Add(project);
        await db.SaveChangesAsync();

        _projectId = project.Id;
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    // ==================================================== only approved may go

    [Fact]
    public async Task ADraftContractCannotBeSent()
    {
        if (!Available) return;

        var contract = await NewContractAsync(approvedWording: null);

        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);

        // A draft is not an offer. Nothing is issued and the refusal names the
        // remedy rather than saying "cannot be sent".
        Assert.False(result.Succeeded);
        Assert.Equal(DeliveryRefusal.NotApproved, result.Refusal);
        Assert.Contains("freigegebene Fassung", result.Message!, StringComparison.Ordinal);
        Assert.Null(result.SigningUrl);
    }

    [Fact]
    public async Task AnApprovedContractCanBeSent()
    {
        if (!Available) return;

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");

        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.SentVersion);
        Assert.NotNull(result.SigningUrl);
    }

    [Fact]
    public async Task AManuallyCreatedContractIsNoDifferent()
    {
        if (!Available) return;

        // Nothing about delivery asks how the wording was produced. A contract
        // typed by hand and approved is as sendable as a generated one.
        var contract = await NewContractAsync(
            "## 1. Leistungsbeschreibung\n\nVon Hand erfasste Leistungen.", generatedBy: "manual");

        Assert.True((await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null)).Succeeded);
    }

    [Fact]
    public async Task AnAlreadySignedContractCannotBeSentAgain()
    {
        if (!Available) return;

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");

        await _db!.Contracts.Where(c => c.Id == contract)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, DocumentStatus.Signed));

        // ExecuteUpdate goes round the change tracker, and this context still
        // holds the row as it was. A request gets a fresh context; a test does
        // not.
        _db.ChangeTracker.Clear();

        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);

        Assert.Equal(DeliveryRefusal.AlreadySigned, result.Refusal);
    }

    [Fact]
    public async Task AnApprovedVersionWithNoWordingCannotBeSent()
    {
        if (!Available) return;

        var contract = await NewContractAsync("   ");

        Assert.Equal(
            DeliveryRefusal.NoContent,
            (await Delivery().CreateLinkAsync(
                contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null)).Refusal);
    }

    // ========================================================== the snapshot

    [Fact]
    public async Task TheWordingIsFrozenWhenTheLinkIsIssued()
    {
        if (!Available) return;

        const string sent = "## 1. Leistungsbeschreibung\n\nBetreuung wie vereinbart.";

        var contract = await NewContractAsync(sent);

        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);

        var request = await RequestAsync(result.RequestId!.Value);

        Assert.Equal(sent, request.SnapshotMarkdown);
        Assert.Equal(ContractDeliveryService.Sha256(sent), request.SnapshotHash);
        Assert.Equal(1, request.IssuedForDraftVersion);
    }

    [Fact]
    public async Task ChangingTheContractAfterwardsDoesNotChangeWhatWasSent()
    {
        if (!Available) return;

        const string sent = "## 1. Leistungsbeschreibung\n\nBetreuung wie vereinbart.";

        var contract = await NewContractAsync(sent);

        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);

        // The contract moves on, the way it does when somebody regenerates or
        // translates it.
        await _db!.Contracts.Where(c => c.Id == contract)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Terms, "## 1. Etwas völlig anderes"));

        var request = await RequestAsync(result.RequestId!.Value);

        // The customer holding that link still has the document they were sent.
        Assert.Equal(sent, request.SnapshotMarkdown);
        Assert.DoesNotContain("völlig anderes", request.SnapshotMarkdown!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTermsInForceAreRecordedWithTheRequest()
    {
        if (!Available) return;

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");

        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);

        var request = await RequestAsync(result.RequestId!.Value);

        // A URL on its own proves nothing: the page behind it can change the day
        // after somebody signs.
        Assert.Equal("https://netwitcher.com/de/agb-fuer-agenturen", request.TermsUrl);
        Assert.Equal("2026-01", request.TermsVersion);
        Assert.Equal("abc123", request.TermsHash);
    }

    // ============================================================== the token

    [Fact]
    public async Task OnlyTheHashOfTheTokenIsStored()
    {
        if (!Available) return;

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");

        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);

        var token = new Uri(result.SigningUrl!).Query.Split("t=")[1];
        var request = await RequestAsync(result.RequestId!.Value);

        // A copy of the database is not a set of signing links.
        Assert.NotEqual(token, request.TokenHash);
        Assert.Equal(ContractAccessLink.HashToken(Uri.UnescapeDataString(token)), request.TokenHash);

        // And nothing else on the row carries it either.
        Assert.DoesNotContain(token, request.SnapshotMarkdown ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(token, request.RecipientEmail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLinkUsesTheConfiguredPublicUrlAndNeverLocalhost()
    {
        if (!Available) return;

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");

        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);

        Assert.StartsWith(PublicUrl + "/contracts/sign/", result.SigningUrl!);
        Assert.DoesNotContain("localhost", result.SigningUrl!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoPublicUrlMeansNoLink()
    {
        if (!Available) return;

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");

        // Building it from the request's Host header is how a forged header
        // sends a customer's signing link to somebody else's site.
        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.CopiedLink, "", null);

        Assert.Equal(DeliveryRefusal.NoPublicUrl, result.Refusal);
        Assert.Null(result.SigningUrl);
    }

    [Fact]
    public async Task TwoTokensAreNeverTheSame()
    {
        if (!Available) return;

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");
        var delivery = Delivery();

        var first = await delivery.CreateLinkAsync(contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);
        var second = await delivery.CreateLinkAsync(contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);

        Assert.NotEqual(first.SigningUrl, second.SigningUrl);
    }

    [Fact]
    public async Task IssuingANewLinkRetiresTheOldOne()
    {
        if (!Available) return;

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");
        var delivery = Delivery();

        var first = await delivery.CreateLinkAsync(contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);
        await delivery.CreateLinkAsync(contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);

        var old = await RequestAsync(first.RequestId!.Value);

        // Two live links to one contract is two documents somebody could sign.
        Assert.NotNull(old.RevokedAtUtc);
        Assert.Equal(ContractSignatureRequestStatus.Superseded, old.Status);
        Assert.False(old.AllowsSigning(DateTimeOffset.UtcNow));
    }

    // =========================================================== the recipient

    [Fact]
    public async Task TheCustomersOwnAddressIsUsedWithoutBeingRetyped()
    {
        if (!Available) return;

        await AddContactAsync("kunde@example.com", "Frau Muster", primary: true);

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");

        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.Email, PublicUrl, null);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("kunde@example.com", (await RequestAsync(result.RequestId!.Value)).RecipientEmail);
    }

    [Fact]
    public async Task NoAddressMeansNoEmail()
    {
        if (!Available) return;

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");

        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.Email, PublicUrl, null);

        Assert.Equal(DeliveryRefusal.NoRecipient, result.Refusal);
    }

    [Fact]
    public async Task SeveralAddressesRequireAChoice()
    {
        if (!Available) return;

        await AddContactAsync("eine@example.com", "Frau Muster", primary: false);
        await AddContactAsync("andere@example.com", "Herr Muster", primary: false);

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");

        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.Email, PublicUrl, null);

        // Picking one for them is how a contract reaches the wrong person at a
        // customer.
        Assert.Equal(DeliveryRefusal.AmbiguousRecipient, result.Refusal);
        Assert.Equal(2, result.Choices.Count);
    }

    [Fact]
    public async Task AChosenAddressIsHonoured()
    {
        if (!Available) return;

        await AddContactAsync("eine@example.com", "Frau Muster", primary: false);
        await AddContactAsync("andere@example.com", "Herr Muster", primary: false);

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");

        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.Email, PublicUrl, null, "andere@example.com");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("andere@example.com", (await RequestAsync(result.RequestId!.Value)).RecipientEmail);
    }

    [Fact]
    public async Task AnAddressThatIsNotTheCustomersIsRefused()
    {
        if (!Available) return;

        await AddContactAsync("kunde@example.com", "Frau Muster", primary: true);

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");

        // Otherwise this is a way to send somebody else's contract anywhere.
        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.Email, PublicUrl, null, "angreifer@example.com");

        Assert.Equal(DeliveryRefusal.UnknownRecipient, result.Refusal);
    }

    [Theory]
    [InlineData("kunde@example.com", true)]
    [InlineData("vorname.nachname@sub.example.co.uk", true)]
    [InlineData("kunde@example", false)]
    [InlineData("kunde@@example.com", false)]
    [InlineData("kunde @example.com", false)]
    [InlineData("@example.com", false)]
    [InlineData("kunde@", false)]
    [InlineData("", false)]
    public void AddressesAreValidatedOnTheServer(string address, bool valid) =>
        Assert.Equal(valid, ContractDeliveryService.IsEmail(address));

    // ======================================================== failed delivery

    [Fact]
    public async Task AFailedEmailIsNotRecordedAsSent()
    {
        if (!Available) return;

        await AddContactAsync("kunde@example.com", "Frau Muster", primary: true);

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");
        var delivery = Delivery();

        var result = await delivery.CreateLinkAsync(
            contract, ContractDeliveryMethod.Email, PublicUrl, null);

        await delivery.MarkDeliveryFailedAsync(result.RequestId!.Value, "REF123");

        var request = await RequestAsync(result.RequestId.Value);

        // The page must never tell the owner a customer has been written to when
        // nobody has — and the token that went nowhere is dead.
        Assert.Null(request.SentAtUtc);
        Assert.NotEqual(ContractSignatureRequestStatus.Sent, request.Status);
        Assert.NotNull(request.RevokedAtUtc);
        Assert.False(request.AllowsSigning(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task ASuccessfulEmailIsRecordedAsSent()
    {
        if (!Available) return;

        await AddContactAsync("kunde@example.com", "Frau Muster", primary: true);

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");
        var delivery = Delivery();

        var result = await delivery.CreateLinkAsync(
            contract, ContractDeliveryMethod.Email, PublicUrl, null);

        await delivery.MarkSentAsync(result.RequestId!.Value);

        var request = await RequestAsync(result.RequestId.Value);

        Assert.Equal(ContractSignatureRequestStatus.Sent, request.Status);
        Assert.NotNull(request.SentAtUtc);
        Assert.True(request.AllowsSigning(DateTimeOffset.UtcNow));
    }

    // ============================================================ cancelling

    [Fact]
    public async Task CancellingKillsTheLink()
    {
        if (!Available) return;

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");
        var delivery = Delivery();

        var result = await delivery.CreateLinkAsync(
            contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);

        Assert.True((await delivery.CancelAsync(result.RequestId!.Value, null)).Succeeded);

        var request = await RequestAsync(result.RequestId.Value);

        Assert.Equal(ContractSignatureRequestStatus.Cancelled, request.Status);
        Assert.NotNull(request.CancelledAtUtc);
        Assert.False(request.AllowsSigning(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task AnAlreadySignedRequestCannotBeCancelled()
    {
        if (!Available) return;

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");
        var delivery = Delivery();

        var result = await delivery.CreateLinkAsync(
            contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);

        await _db!.Set<ContractAccessLink>().Where(l => l.Id == result.RequestId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ConsumedAtUtc, DateTimeOffset.UtcNow));

        _db.ChangeTracker.Clear();

        Assert.Equal(
            DeliveryRefusal.AlreadySigned,
            (await delivery.CancelAsync(result.RequestId!.Value, null)).Refusal);
    }

    // ======================================================= expiry and reuse

    [Fact]
    public async Task AnExpiredRequestAllowsNothing()
    {
        if (!Available) return;

        var request = new ContractAccessLink
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            Status = ContractSignatureRequestStatus.Sent
        };

        Assert.False(request.AllowsSigning(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task AConsumedRequestAllowsNothing()
    {
        if (!Available) return;

        var request = new ContractAccessLink
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            Status = ContractSignatureRequestStatus.Sent,
            ConsumedAtUtc = DateTimeOffset.UtcNow
        };

        // Single use. A link that has signed once cannot sign again.
        Assert.False(request.AllowsSigning(DateTimeOffset.UtcNow));
    }

    // =============================================================== the audit

    [Fact]
    public async Task IssuingALinkIsAuditedWithoutTheToken()
    {
        if (!Available) return;

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");

        var result = await Delivery().CreateLinkAsync(
            contract, ContractDeliveryMethod.CopiedLink, PublicUrl, null);

        var token = new Uri(result.SigningUrl!).Query.Split("t=")[1];

        var audit = await _db!.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityId == contract)
            .ToListAsync();

        var created = Assert.Single(audit, a => a.Action == "SignatureLinkCreated");
        var recorded = created.AfterData!.RootElement.ToString();

        // Enough to reconstruct what happened, and nothing that could be used to
        // sign with.
        Assert.Contains(result.RequestId!.Value.ToString(), recorded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("snapshotHash", recorded, StringComparison.Ordinal);
        Assert.DoesNotContain(token, recorded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendingByEmailIsAuditedSeparatelyFromTheSendSucceeding()
    {
        if (!Available) return;

        await AddContactAsync("kunde@example.com", "Frau Muster", primary: true);

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");
        var delivery = Delivery();

        var result = await delivery.CreateLinkAsync(
            contract, ContractDeliveryMethod.Email, PublicUrl, null);

        await delivery.MarkSentAsync(result.RequestId!.Value);

        var actions = await _db!.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityId == contract)
            .OrderBy(a => a.CreatedAt)
            .Select(a => a.Action)
            .ToListAsync();

        // Requested, then sent — in that order, so a failure between them is
        // visible as a request with no send.
        Assert.Equal(["SignatureEmailRequested", "SignatureEmailSent"], actions);
    }

    // =========================================================== the UI state

    [Fact]
    public async Task ProjectDetailsIsToldWhatItNeedsToDrawTheSection()
    {
        if (!Available) return;

        await AddContactAsync("kunde@example.com", "Frau Muster", primary: true);

        var contract = await NewContractAsync("## 1. Leistungsbeschreibung\n\nBetreuung.");
        var delivery = Delivery();

        await delivery.CreateLinkAsync(contract, ContractDeliveryMethod.Email, PublicUrl, null);

        var state = await delivery.DescribeAsync(contract);

        Assert.True(state.CanSend);
        Assert.True(state.CanEmail);
        Assert.Equal(1, state.ApprovedVersion);
        Assert.Equal("Musterfirma GmbH", state.CustomerName);
        Assert.Equal("kunde@example.com", state.LatestRequestRecipient);
        Assert.NotNull(state.LatestRequestExpiresAt);
        Assert.False(state.IsSigned);
    }

    [Fact]
    public async Task ADraftContractTellsTheSectionItCannotBeSent()
    {
        if (!Available) return;

        var state = await Delivery().DescribeAsync(await NewContractAsync(approvedWording: null));

        Assert.False(state.CanSend);
        Assert.False(state.CanEmail);
        Assert.Equal(DeliveryRefusal.NotApproved, state.Refusal);
    }

    // =================================================================== helpers

    private ContractDeliveryService Delivery() =>
        new(_db!,
            Options.Create(new ContractTermsOptions
            {
                Url = "https://netwitcher.com/de/agb-fuer-agenturen",
                Version = "2026-01",
                Hash = "abc123",
                LinkValidDays = 14
            }),
            NullLogger<ContractDeliveryService>.Instance);

    private async Task<ContractAccessLink> RequestAsync(Guid id)
    {
        _db!.ChangeTracker.Clear();

        return await _db.Set<ContractAccessLink>().AsNoTracking().FirstAsync(l => l.Id == id);
    }

    private async Task AddContactAsync(string email, string name, bool primary)
    {
        _db!.Add(new CustomerContact
        {
            Id = Guid.NewGuid(),
            CustomerId = _customerId,
            Name = name,
            Email = email,
            IsPrimary = primary
        });

        await _db.SaveChangesAsync();
    }

    private async Task<Guid> NewContractAsync(string? approvedWording, string generatedBy = "openai")
    {
        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            ProjectId = _projectId,
            ContractNo = "C-" + Guid.NewGuid().ToString("n")[..8],
            Status = DocumentStatus.Draft,
            Currency = "EUR",
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2027, 3, 31)
        };

        _db!.Add(contract);

        if (approvedWording is not null)
        {
            _db.Add(new ContractDraft
            {
                Id = Guid.NewGuid(),
                ContractId = contract.Id,
                Version = 1,
                DocumentMarkdown = approvedWording,
                GeneratedBy = generatedBy,
                GeneratedAt = DateTimeOffset.UtcNow,
                Kind = ContractDraftKind.Generated,
                IsApproved = true,
                Status = ContractDraftStatus.Approved,
                ApprovedAt = DateTimeOffset.UtcNow
            });

            contract.Terms = approvedWording;
        }

        await _db.SaveChangesAsync();

        return contract.Id;
    }
}
