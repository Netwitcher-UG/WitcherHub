using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using WitcherHub.Application.Interfaces;
using WitcherHub.Infrastructure.Services.OpenAI;

namespace WitcherHub.Tests
{
    /// <summary>
    /// The owner sent a screenshot reading "The assistant is rate limited or out
    /// of quota. Try again shortly." — after they had just corrected the model
    /// name in Railway. That one sentence covered three unrelated faults, and the
    /// only advice it gave ("try again shortly") was wrong for two of them:
    ///
    ///   * an actual rate limit clears by waiting,
    ///   * an exhausted account never clears until credit is added,
    ///   * a wrong model id never clears until the variable is corrected.
    ///
    /// The provider reports the first two with the *same* HTTP 429, so the body
    /// is the only thing that separates them. These tests pin that separation and
    /// the advice each one gives, because getting it wrong sends the owner into a
    /// retry loop against a fault that cannot clear on its own.
    /// </summary>
    public class AiFailureClassificationTests
    {
        // ---------------------------------------------------------------
        // 429: rate limit vs. exhausted account
        // ---------------------------------------------------------------

        [Fact]
        public void PlainRateLimitIsRateLimited()
        {
            var (kind, status) = OpenAiTextGenerator.Classify(Http(429, """
                {"error":{"message":"Rate limit reached for gpt-4o in organization org-x on requests per min (RPM): Limit 500, Used 500.","type":"requests","code":"rate_limit_exceeded"}}
                """));

            Assert.Equal(AiFailureKind.RateLimited, kind);
            Assert.Equal(429, status);
        }

        [Theory]
        [InlineData("""{"error":{"message":"You exceeded your current quota, please check your plan and billing details.","type":"insufficient_quota","code":"insufficient_quota"}}""")]
        [InlineData("""{"error":{"message":"Billing hard limit has been reached","type":"invalid_request_error","code":"billing_hard_limit_reached"}}""")]
        [InlineData("""{"error":{"message":"Your account is not active, please check your billing details.","code":"billing_not_active"}}""")]
        public void ExhaustedAccountIsNotTreatedAsARateLimit(string body)
        {
            var (kind, status) = OpenAiTextGenerator.Classify(Http(429, body));

            Assert.Equal(AiFailureKind.QuotaExhausted, kind);
            Assert.Equal(429, status);
        }

        [Fact]
        public void QuotaIsDetectedWhenTheBodyOnlyReachesUsThroughTheMessage()
        {
            // Not every path through the client hands us a readable response
            // stream; on those the body is already inlined into the message.
            var ex = new ClientResultException(
                "HTTP 429 (insufficient_quota): You exceeded your current quota.",
                Http(429, "").GetRawResponse());

            Assert.Equal(AiFailureKind.QuotaExhausted, OpenAiTextGenerator.Classify(ex).Kind);
        }

        // ---------------------------------------------------------------
        // The other statuses
        // ---------------------------------------------------------------

        [Theory]
        [InlineData(401, AiFailureKind.Authentication)]
        [InlineData(403, AiFailureKind.Authentication)]
        [InlineData(404, AiFailureKind.ModelUnavailable)]
        [InlineData(408, AiFailureKind.Timeout)]
        [InlineData(500, AiFailureKind.ServiceError)]
        [InlineData(503, AiFailureKind.ServiceError)]
        public void StatusCodesMapToTheirOwnKind(int status, AiFailureKind expected)
        {
            Assert.Equal(expected, OpenAiTextGenerator.Classify(Http(status, "{}")).Kind);
        }

        [Fact]
        public void UnknownModelNameIsReportedAsAModelProblemNotAMystery()
        {
            var (kind, _) = OpenAiTextGenerator.Classify(Http(404, """
                {"error":{"message":"The model `gpt-5.6-sol` does not exist or you do not have access to it.","code":"model_not_found"}}
                """));

            Assert.Equal(AiFailureKind.ModelUnavailable, kind);
        }

        [Fact]
        public void BillingStopArrivingAsA400IsStillABillingStop()
        {
            var (kind, _) = OpenAiTextGenerator.Classify(Http(400, """
                {"error":{"code":"billing_hard_limit_reached"}}
                """));

            Assert.Equal(AiFailureKind.QuotaExhausted, kind);
        }

        [Fact]
        public void PlainBadRequestStillPointsAtTheModel()
        {
            Assert.Equal(
                AiFailureKind.ModelUnavailable,
                OpenAiTextGenerator.Classify(Http(400, """{"error":{"message":"Unsupported parameter."}}""")).Kind);
        }

        [Fact]
        public void TransportFailuresAreSeparatedFromProviderFailures()
        {
            Assert.Equal(AiFailureKind.Network, OpenAiTextGenerator.Classify(new HttpRequestException("no route")).Kind);
            Assert.Equal(AiFailureKind.Timeout, OpenAiTextGenerator.Classify(new TaskCanceledException()).Kind);
            Assert.Equal(AiFailureKind.NotConfigured, OpenAiTextGenerator.Classify(new InvalidOperationException("no key")).Kind);
        }

        [Fact]
        public void AWrappedFailureIsClassifiedByWhatIsInside()
        {
            var wrapped = new InvalidOperationException(
                "call failed", Http(429, """{"error":{"code":"insufficient_quota"}}"""));

            // The outer InvalidOperationException would otherwise read as
            // "not configured", which is a different fix entirely.
            Assert.Equal(AiFailureKind.QuotaExhausted, OpenAiTextGenerator.Classify(wrapped).Kind);
        }

        // ---------------------------------------------------------------
        // What the user is told, and whether we offer a retry
        // ---------------------------------------------------------------

        [Fact]
        public void EachThreeSixNineFaultGivesDifferentAdvice()
        {
            var rateLimited = new AiInvocationException(AiFailureKind.RateLimited, "x", "AAA11111");
            var noCredit = new AiInvocationException(AiFailureKind.QuotaExhausted, "x", "BBB22222");
            var badModel = new AiInvocationException(AiFailureKind.ModelUnavailable, "x", "CCC33333");

            // The old text was one sentence for all three. Whatever the wording
            // becomes, they must not converge again.
            Assert.NotEqual(rateLimited.UserMessage, noCredit.UserMessage);
            Assert.NotEqual(noCredit.UserMessage, badModel.UserMessage);

            // Waiting is the fix for exactly one of them.
            Assert.Contains("wait", rateLimited.UserMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("try again", noCredit.UserMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("try again", badModel.UserMessage, StringComparison.OrdinalIgnoreCase);

            // Each one names where to go.
            Assert.Contains("Billing", noCredit.UserMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OpenAI__Model", badModel.UserMessage);
        }

        [Fact]
        public void FaultsOnlyTheOwnerCanClearAreNotOfferedAsRetryable()
        {
            foreach (var kind in new[]
                     {
                         AiFailureKind.NotConfigured,
                         AiFailureKind.Authentication,
                         AiFailureKind.ModelUnavailable,
                         AiFailureKind.QuotaExhausted
                     })
            {
                var failure = new AiInvocationException(kind, "x", "DDD44444");

                Assert.True(failure.NeedsOwnerAction, $"{kind} needs the owner");
                Assert.False(failure.IsTransient, $"{kind} must not be retried");
            }
        }

        [Fact]
        public void FaultsThatPassOnTheirOwnStayRetryable()
        {
            foreach (var kind in new[]
                     {
                         AiFailureKind.RateLimited,
                         AiFailureKind.Timeout,
                         AiFailureKind.Network,
                         AiFailureKind.ServiceError
                     })
            {
                var failure = new AiInvocationException(kind, "x", "EEE55555");

                Assert.True(failure.IsTransient, $"{kind} is worth retrying");
                Assert.False(failure.NeedsOwnerAction, $"{kind} is not a configuration fault");
            }
        }

        [Fact]
        public void EveryMessageCarriesTheReferenceAndNoTechnicalDetail()
        {
            foreach (var kind in Enum.GetValues<AiFailureKind>())
            {
                var message = new AiInvocationException(kind, "ClientResultException", "FFF66666").UserMessage;

                Assert.Contains("FFF66666", message);
                Assert.DoesNotContain("ClientResultException", message);
                Assert.DoesNotContain("sk-", message);
            }
        }

        // ---------------------------------------------------------------
        // Redaction
        // ---------------------------------------------------------------

        [Fact]
        public void AKeyQuotedBackByTheProviderNeverReachesTheLog()
        {
            var redacted = OpenAiTextGenerator.Redact(
                "Incorrect API key provided: sk-proj-AbCdEf0123456789XyZ. You can find your API key at ...");

            Assert.DoesNotContain("AbCdEf0123456789XyZ", redacted);
            Assert.Contains("sk-***", redacted);
        }

        /// <summary>
        /// The client masks the key itself before quoting it back, and that masked
        /// form used to travel into the log untouched: the pattern demanded eight
        /// key characters in a row and the mask supplies five, then asterisks.
        /// Partial key material in a log this application says it does not write
        /// is still a broken promise, so the masked form is stripped too.
        /// </summary>
        [Fact]
        public void TheClientsOwnMaskedKeyIsStrippedAsWell()
        {
            var redacted = OpenAiTextGenerator.Redact(
                "Incorrect API key provided: sk-FAKEK**********************0000. " +
                "You can find your API key at https://platform.openai.com/account/api-keys.");

            Assert.DoesNotContain("FAKEK", redacted);
            Assert.DoesNotContain("0000", redacted);
            Assert.Contains("sk-***", redacted);

            // The rest of the message is what makes the failure diagnosable, so it
            // has to survive.
            Assert.Contains("Incorrect API key provided", redacted);
        }

        // ---------------------------------------------------------------

        private static ClientResultException Http(int status, string body) =>
            new(new FakeResponse(status, body));

        /// <summary>
        /// The smallest thing <see cref="ClientResultException"/> accepts: a
        /// status and a body.
        /// </summary>
        private sealed class FakeResponse : PipelineResponse
        {
            private readonly BinaryData _content;

            public FakeResponse(int status, string body)
            {
                Status = status;
                _content = BinaryData.FromString(body);
                ContentStream = new MemoryStream(Encoding.UTF8.GetBytes(body));
            }

            public override int Status { get; }
            public override string ReasonPhrase => "";
            public override Stream? ContentStream { get; set; }
            public override BinaryData Content => _content;

            protected override PipelineResponseHeaders HeadersCore { get; } = new FakeHeaders();

            public override BinaryData BufferContent(CancellationToken ct = default) => _content;

            public override ValueTask<BinaryData> BufferContentAsync(CancellationToken ct = default) =>
                new(_content);

            public override void Dispose() => ContentStream?.Dispose();
        }

        private sealed class FakeHeaders : PipelineResponseHeaders
        {
            public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
                Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();

            public override bool TryGetValue(string name, out string? value)
            {
                value = null;
                return false;
            }

            public override bool TryGetValues(string name, out IEnumerable<string>? values)
            {
                values = null;
                return false;
            }
        }
    }
}
