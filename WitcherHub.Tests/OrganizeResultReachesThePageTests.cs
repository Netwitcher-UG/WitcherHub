using System.Text.Json;
using WitcherHub.Application.Models.DTO.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests
{
    /// <summary>
    /// What "Organize with AI" hands back to the page.
    ///
    /// The organizer's answer used to be returned straight from the page handler,
    /// so MVC serialised it — and MVC is configured with a
    /// <c>JsonStringEnumConverter</c>. Moving the work onto the background queue
    /// moved the serialising into the job service, whose options did not have
    /// that converter, and the status handler writes the stored JSON through
    /// verbatim. So the browser began receiving <c>"pricingModel": 0</c> where it
    /// had always received <c>"pricingModel": "Fixed"</c>.
    ///
    /// Nothing threw, which is why it was easy to miss. The page compares these
    /// as strings:
    ///
    ///   * <c>lineNet</c> prices a line as quantity times rate unless the model
    ///     equals "Fixed", so a fixed fee was multiplied by its quantity;
    ///   * <c>shape</c> decides which fields a position even shows from the same
    ///     comparison;
    ///   * every &lt;select&gt; is built from a list of names, and a number
    ///     matches none of them, so the dropdowns came up empty.
    ///
    /// These tests are about the wire format the page actually depends on.
    /// </summary>
    public class OrganizeResultReachesThePageTests
    {
        /// <summary>
        /// The options the job service stores results with. Read from the service
        /// itself rather than copied, so a change there fails here.
        /// </summary>
        private static string Serialize(object value)
        {
            var options = typeof(WitcherHub.Infrastructure.Services.Contracts.ContractAiJobService)
                .GetField("Json", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.GetValue(null) as JsonSerializerOptions;

            Assert.NotNull(options);

            return JsonSerializer.Serialize(value, options!);
        }

        private static ManualPositionDto APosition() => new()
        {
            Position = 1,
            Title = "Monatliche Betreuung",
            PricingModel = PricingModel.Fixed,
            BillingCycle = BillingCycle.Monthly,
            ActivationMethod = ActivationMethod.AfterSignature,
            DiscountType = DiscountType.Percent,
            DiscountValue = 10m,
            UnitPrice = 500m,
            Currency = "EUR"
        };

        [Fact]
        public void EveryEnumArrivesAsItsNameAndNotAsANumber()
        {
            var json = Serialize(APosition());

            // The four the page reads back as strings.
            Assert.Contains("\"pricingModel\":\"Fixed\"", json);
            Assert.Contains("\"billingCycle\":\"Monthly\"", json);
            Assert.Contains("\"activationMethod\":\"AfterSignature\"", json);
            Assert.Contains("\"discountType\":\"Percent\"", json);

            // And none of them as the number that broke the pricing and the
            // dropdowns.
            Assert.DoesNotContain("\"pricingModel\":0", json);
            Assert.DoesNotContain("\"billingCycle\":1", json);
        }

        [Fact]
        public void TheNamesAreTheOnesThePageMatchesAgainst()
        {
            var script = File.ReadAllText(Path.Combine(
                TestPaths.WebProject, "wwwroot", "js", "pages", "contracts", "positions-builder.js"));

            var json = Serialize(APosition());

            // The page builds its dropdowns from these lists and prices a line by
            // comparing against "Fixed". A value that is not in the list shows as
            // an empty select; a pricing model that is not "Fixed" is multiplied
            // by its quantity.
            Assert.Contains("BILLING_CYCLES = [\"OneTime\", \"Monthly\"", script);
            Assert.Contains("PRICING_MODELS = [\"Fixed\"", script);
            Assert.Contains("p.pricingModel === \"Fixed\"", script);

            foreach (var name in new[] { "Fixed", "Monthly", "AfterSignature" })
                Assert.Contains($"\"{name}\"", json);
        }

        [Fact]
        public void TheWholeAnswerKeepsTheShapeThePageReads()
        {
            // The keys showReview and the apply step index into. Camel-cased by
            // the same options, so a change to those breaks this too.
            var answer = Serialize(new
            {
                positions = new[] { APosition() },
                changes = new[] { new { PositionTitle = "A", Field = "title", Before = "x", After = "y", kind = "Reworded" } },
                rejected = new[] { new { PositionTitle = "A", Field = "unitPrice", Before = "1", After = "2" } },
                model = "test-model"
            });

            foreach (var key in new[]
                     {
                         "\"positions\"", "\"changes\"", "\"rejected\"", "\"model\"",
                         "\"positionTitle\"", "\"field\"", "\"before\"", "\"after\"", "\"kind\""
                     })
            {
                Assert.Contains(key, answer);
            }
        }

        [Fact]
        public void AJobQueuedBeforeThisChangeIsStillReadable()
        {
            // The converter reads numbers as well as names, so a job whose request
            // was written by the previous build does not become unreadable the
            // moment this one deploys.
            var options = typeof(WitcherHub.Infrastructure.Services.Contracts.ContractAiJobService)
                .GetField("Json", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.GetValue(null) as JsonSerializerOptions;

            var numeric = """
                {"position":1,"title":"Alt","pricingModel":0,"billingCycle":1,"unitPrice":500}
                """;

            var read = JsonSerializer.Deserialize<ManualPositionDto>(numeric, options!);

            Assert.NotNull(read);
            Assert.Equal(PricingModel.Fixed, read!.PricingModel);
            Assert.Equal(BillingCycle.Monthly, read.BillingCycle);
        }
    }
}
