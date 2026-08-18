namespace WitcherHub.Application.Interfaces
{
    public interface IAiTextGenerator
    {
        Task<string> GenerateTextAsync(string prompt);
    }

    /// <summary>
    /// Why a call to the model did not produce an answer.
    ///
    /// "The assistant is not reachable right now" was true of every one of these
    /// and useful for none of them: a missing API key, a model name the account
    /// cannot use, and a rate limit are three different problems with three
    /// different fixes, and the user was told the same sentence for all three.
    /// </summary>
    public enum AiFailureKind
    {
        /// <summary>Nothing more specific could be determined.</summary>
        Unknown = 0,

        /// <summary>No API key is configured, so no call was attempted.</summary>
        NotConfigured = 1,

        /// <summary>The key was rejected — wrong, revoked, or for another account.</summary>
        Authentication = 2,

        /// <summary>The configured model does not exist or this account cannot use it.</summary>
        ModelUnavailable = 3,

        /// <summary>Too many requests in too short a time. Waiting fixes it.</summary>
        RateLimited = 4,

        /// <summary>
        /// The account has no credit left, or a spending limit has been reached.
        ///
        /// The provider reports this with the same HTTP 429 as ordinary rate
        /// limiting, which is why the two used to be reported as one thing — but
        /// they need opposite responses. Rate limiting clears by waiting; an
        /// exhausted quota does not clear at all until somebody adds credit, so
        /// retrying it wastes time and makes a permanent fault look intermittent.
        /// </summary>
        QuotaExhausted = 9,

        /// <summary>The call took too long or was cut off.</summary>
        Timeout = 5,

        /// <summary>The service could not be reached at all.</summary>
        Network = 6,

        /// <summary>A reply arrived but could not be used.</summary>
        BadResponse = 7,

        /// <summary>The service reported a fault on its side.</summary>
        ServiceError = 8
    }

    /// <summary>
    /// A model call that failed, carrying enough to act on.
    ///
    /// The correlation id is the one thing the user sees; it is written to the
    /// log next to the technical detail, so a screenshot of the error is enough
    /// to find the exact failure. The prompt is deliberately not carried here —
    /// it contains contract text and customer data, and this object ends up in
    /// log output.
    /// </summary>
    public sealed class AiInvocationException : Exception
    {
        public AiInvocationException(
            AiFailureKind kind,
            string technicalDetail,
            string correlationId,
            int? statusCode = null,
            Exception? inner = null)
            : base(technicalDetail, inner)
        {
            Kind = kind;
            CorrelationId = correlationId;
            StatusCode = statusCode;
        }

        public AiFailureKind Kind { get; }
        public string CorrelationId { get; }
        public int? StatusCode { get; }

        /// <summary>
        /// True when trying again later is reasonable. A missing key or an
        /// unusable model will not fix itself, so retrying is not offered.
        /// </summary>
        public bool IsTransient =>
            Kind is AiFailureKind.RateLimited or AiFailureKind.Timeout
                 or AiFailureKind.Network or AiFailureKind.ServiceError
                 or AiFailureKind.BadResponse or AiFailureKind.Unknown;

        /// <summary>
        /// True when the fault is in configuration or billing rather than in the
        /// moment: the same call will fail the same way until somebody changes a
        /// setting or adds credit. Retrying these is what turns a fixed problem
        /// into one that looks intermittent.
        /// </summary>
        public bool NeedsOwnerAction =>
            Kind is AiFailureKind.NotConfigured or AiFailureKind.Authentication
                 or AiFailureKind.ModelUnavailable or AiFailureKind.QuotaExhausted;

        /// <summary>
        /// What to show the user: the actual problem, what to do about it, and
        /// the reference that ties it to the log. Never the key, the prompt, or
        /// the provider's raw response.
        /// </summary>
        public string UserMessage => Kind switch
        {
            AiFailureKind.NotConfigured =>
                "The assistant is not set up on this environment: no OpenAI API key is configured. " +
                $"Set OpenAI__ApiKey and restart. Reference {CorrelationId}.",

            AiFailureKind.Authentication =>
                "The assistant rejected our credentials. The configured OpenAI API key is not valid for this " +
                $"account. Reference {CorrelationId}.",

            AiFailureKind.ModelUnavailable =>
                "The model named in OpenAI__Model does not exist, or this account cannot use it. Check the " +
                "exact id against the model list at platform.openai.com and set OpenAI__Model to one of " +
                $"those. Reference {CorrelationId}.",

            AiFailureKind.RateLimited =>
                "The assistant is receiving requests faster than the account allows. Wait a moment and try " +
                $"again — nothing is wrong with the setup. Reference {CorrelationId}.",

            AiFailureKind.QuotaExhausted =>
                "The OpenAI account has no credit left, or its spending limit has been reached, so the " +
                "assistant refused the request. Trying again will not help until credit is added at " +
                $"platform.openai.com under Billing. Reference {CorrelationId}.",

            AiFailureKind.Timeout =>
                $"The assistant took too long to answer. Try again. Reference {CorrelationId}.",

            AiFailureKind.Network =>
                "The assistant could not be reached from this environment, which usually means outbound " +
                $"network access is blocked. Reference {CorrelationId}.",

            AiFailureKind.BadResponse =>
                $"The assistant answered with something unusable. Try again. Reference {CorrelationId}.",

            AiFailureKind.ServiceError =>
                $"The assistant reported a fault on its side. Try again shortly. Reference {CorrelationId}.",

            _ => $"The assistant could not be used. Reference {CorrelationId}."
        };
    }
}
