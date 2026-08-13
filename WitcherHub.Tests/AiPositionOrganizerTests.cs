using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Infrastructure.Services.OpenAI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

/// <summary>
/// The organizer is exercised against a stubbed text generator. No test in this
/// file reaches the real OpenAI API.
/// </summary>
public class AiPositionOrganizerTests
{
    private sealed class StubAi : IAiTextGenerator
    {
        private readonly Func<string, Task<string>> _respond;
        public string? LastPrompt { get; private set; }

        public StubAi(string response) => _respond = _ => Task.FromResult(response);
        public StubAi(Func<string, Task<string>> respond) => _respond = respond;

        public Task<string> GenerateTextAsync(string prompt)
        {
            LastPrompt = prompt;
            return _respond(prompt);
        }
    }

    private static AiPositionOrganizer Organizer(IAiTextGenerator ai) =>
        new(ai,
            Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" }),
            NullLogger<AiPositionOrganizer>.Instance);

    private static ManualPositionDto UserPosition() => new()
    {
        ClientId = "pos-1",
        Title = "Website",
        Quantity = 2,
        UnitPrice = 1500m,
        Currency = "EUR",
        VatRate = 19m,
        BillingCycle = BillingCycle.OneTime,
        PricingModel = PricingModel.Unit,
        Position = 1
    };

    // ---- the guard: commercial facts are the user's ------------------------

    [Fact]
    public async Task APriceTheModelChangedIsRevertedAndReported()
    {
        var ai = new StubAi("""
            [{"clientId":"pos-1","title":"Website relaunch","description":"A fresh site",
              "quantity":2,"unitPrice":1900,"currency":"EUR","vatRate":19,
              "billingCycle":"OneTime","pricingModel":"Unit","isFree":false}]
            """);

        var result = await Organizer(ai).OrganizeAsync(new OrganizePositionsRequest
        {
            RoughInput = "new website",
            ExistingPositions = { UserPosition() }
        });

        Assert.True(result.Succeeded);

        // The user's price survives.
        Assert.Equal(1500m, result.Positions.Single().UnitPrice);

        // And the attempt is surfaced rather than swallowed.
        var rejected = Assert.Single(result.RejectedChanges, c => c.Field == "UnitPrice");
        Assert.Equal("1500", rejected.Before);
        Assert.Equal("1900", rejected.After);
        Assert.Equal(PositionChangeKind.RejectedCommercial, rejected.Kind);
    }

    [Theory]
    [InlineData("quantity", "99", nameof(ManualPositionDto.Quantity))]
    [InlineData("vatRate", "7", nameof(ManualPositionDto.VatRate))]
    [InlineData("currency", "\"USD\"", nameof(ManualPositionDto.Currency))]
    [InlineData("billingCycle", "\"Monthly\"", nameof(ManualPositionDto.BillingCycle))]
    [InlineData("discountValue", "50", nameof(ManualPositionDto.DiscountValue))]
    [InlineData("durationPeriods", "36", nameof(ManualPositionDto.DurationPeriods))]
    [InlineData("startDate", "\"2030-01-01\"", nameof(ManualPositionDto.StartDate))]
    [InlineData("isFree", "true", nameof(ManualPositionDto.IsFree))]
    public async Task EveryCommercialFieldIsProtected(string field, string tamperedValue, string expectedField)
    {
        var ai = new StubAi($$"""
            [{"clientId":"pos-1","title":"Website","quantity":2,"unitPrice":1500,
              "currency":"EUR","vatRate":19,"billingCycle":"OneTime","pricingModel":"Unit",
              "isFree":false,"{{field}}":{{tamperedValue}}}]
            """);

        var result = await Organizer(ai).OrganizeAsync(new OrganizePositionsRequest
        {
            ExistingPositions = { UserPosition() },
            RoughInput = "x"
        });

        Assert.True(result.Succeeded);
        Assert.Contains(result.RejectedChanges, c => c.Field == expectedField);
    }

    [Fact]
    public async Task ImprovedWordingIsAcceptedAndFlaggedAsDescriptive()
    {
        var ai = new StubAi("""
            [{"clientId":"pos-1","title":"Website","quantity":2,"unitPrice":1500,
              "currency":"EUR","vatRate":19,"billingCycle":"OneTime","pricingModel":"Unit",
              "isFree":false,
              "description":"Design and build of a responsive marketing website.",
              "deliverables":["Design mockups","Responsive build","Handover"]}]
            """);

        var result = await Organizer(ai).OrganizeAsync(new OrganizePositionsRequest
        {
            ExistingPositions = { UserPosition() },
            RoughInput = "website"
        });

        var position = result.Positions.Single();

        Assert.Equal("Design and build of a responsive marketing website.", position.Description);
        Assert.Equal(3, position.Deliverables.Count);
        Assert.Empty(result.RejectedChanges);
        Assert.Contains(result.Changes, c => c.Kind == PositionChangeKind.Descriptive);
    }

    [Fact]
    public async Task APositionTheModelInventedArrivesWithNoPrice()
    {
        var ai = new StubAi("""
            [{"clientId":"pos-1","title":"Website","quantity":2,"unitPrice":1500,
              "currency":"EUR","vatRate":19,"billingCycle":"OneTime","pricingModel":"Unit","isFree":false},
             {"title":"Monthly maintenance","quantity":12,"unitPrice":250,"currency":"EUR",
              "billingCycle":"Monthly","pricingModel":"Unit","isFree":false}]
            """);

        var result = await Organizer(ai).OrganizeAsync(new OrganizePositionsRequest
        {
            ExistingPositions = { UserPosition() },
            RoughInput = "website"
        });

        var invented = result.Positions.Single(p => p.Title == "Monthly maintenance");

        // Proposed for review, but the model does not get to price work.
        Assert.Null(invented.UnitPrice);
        Assert.False(invented.IsFree);
        Assert.Contains(result.Changes, c => c.Kind == PositionChangeKind.AddedPosition);
    }

    [Fact]
    public async Task APositionTheModelDroppedIsKept()
    {
        var ai = new StubAi("""[{"title":"Something else","quantity":1,"unitPrice":10,"currency":"EUR"}]""");

        var result = await Organizer(ai).OrganizeAsync(new OrganizePositionsRequest
        {
            ExistingPositions = { UserPosition() },
            RoughInput = "x"
        });

        Assert.Contains(result.Positions, p => p.ClientId == "pos-1");
    }

    [Fact]
    public async Task ManualPositionsNeverGainACatalogReference()
    {
        var ai = new StubAi($$"""
            [{"clientId":"pos-1","title":"Website","quantity":2,"unitPrice":1500,"currency":"EUR",
              "vatRate":19,"billingCycle":"OneTime","pricingModel":"Unit","isFree":false,
              "catalogServiceId":"{{Guid.NewGuid()}}"}]
            """);

        var result = await Organizer(ai).OrganizeAsync(new OrganizePositionsRequest
        {
            ExistingPositions = { UserPosition() },
            RoughInput = "x"
        });

        Assert.Null(result.Positions.Single().CatalogServiceId);
    }

    // ---- failure handling: the user's work survives -------------------------

    [Fact]
    public async Task AMalformedResponseFailsWithoutTouchingTheUsersPositions()
    {
        var ai = new StubAi("I'm afraid I can't help with that.");

        var result = await Organizer(ai).OrganizeAsync(new OrganizePositionsRequest
        {
            ExistingPositions = { UserPosition() },
            RoughInput = "x"
        });

        Assert.False(result.Succeeded);
        Assert.True(result.IsTransientFailure);
        Assert.Empty(result.Positions);
        Assert.Contains("unchanged", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ATimeoutIsReportedAsTransient()
    {
        var ai = new StubAi(_ => throw new TaskCanceledException("timed out"));

        var result = await Organizer(ai).OrganizeAsync(new OrganizePositionsRequest
        {
            ExistingPositions = { UserPosition() },
            RoughInput = "x"
        });

        Assert.False(result.Succeeded);
        Assert.True(result.IsTransientFailure);
    }

    [Fact]
    public async Task ARateLimitIsReportedAsTransient()
    {
        var ai = new StubAi(_ => throw new HttpRequestException("429 Too Many Requests"));

        var result = await Organizer(ai).OrganizeAsync(new OrganizePositionsRequest
        {
            ExistingPositions = { UserPosition() },
            RoughInput = "x"
        });

        Assert.False(result.Succeeded);
        Assert.True(result.IsTransientFailure);
    }

    [Fact]
    public async Task CancellationPropagatesRatherThanBeingSwallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ai = new StubAi(_ => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Organizer(ai).OrganizeAsync(
                new OrganizePositionsRequest { ExistingPositions = { UserPosition() }, RoughInput = "x" },
                cts.Token));
    }

    [Fact]
    public async Task NothingToOrganiseIsRejectedBeforeCallingTheModel()
    {
        var ai = new StubAi("[]");

        var result = await Organizer(ai).OrganizeAsync(new OrganizePositionsRequest());

        Assert.False(result.Succeeded);
        Assert.Null(ai.LastPrompt);   // the model was never called
    }

    // ---- schema tolerance ---------------------------------------------------

    [Theory]
    [InlineData("```json\n[{\"title\":\"A\",\"quantity\":1}]\n```")]
    [InlineData("Here you go:\n[{\"title\":\"A\",\"quantity\":1}]\nHope that helps!")]
    public void JsonIsExtractedFromFencedOrChattyResponses(string raw)
    {
        Assert.True(AiPositionOrganizer.TryParsePositions(raw, out var positions, out _));
        Assert.Single(positions);
        Assert.Equal("A", positions[0].Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no json here")]
    [InlineData("[{\"title\": }]")]
    public void UnusableResponsesAreRejected(string raw)
    {
        Assert.False(AiPositionOrganizer.TryParsePositions(raw, out _, out var error));
        Assert.NotNull(error);
    }

    // ---- the prompt must not carry personal data ---------------------------

    [Fact]
    public async Task ThePromptCarriesNoCustomerIdentity()
    {
        var ai = new StubAi("""[{"clientId":"pos-1","title":"Website","quantity":2,"unitPrice":1500,"currency":"EUR"}]""");

        await Organizer(ai).OrganizeAsync(new OrganizePositionsRequest
        {
            ExistingPositions = { UserPosition() },
            RoughInput = "build a website"
        });

        Assert.NotNull(ai.LastPrompt);
        Assert.DoesNotContain("contractItemId", ai.LastPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customerId", ai.LastPrompt, StringComparison.OrdinalIgnoreCase);
    }
}
