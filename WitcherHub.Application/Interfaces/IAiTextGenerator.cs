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

        /// <summary>Too many requests, or the account is out of quota.</summary>
        RateLimited = 4,

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
                "The assistant does not offer the configured model to this account. Set OpenAI__Model to a " +
                $"model the account can use. Reference {CorrelationId}.",

            AiFailureKind.RateLimited =>
                $"The assistant is rate limited or out of quota. Try again shortly. Reference {CorrelationId}.",

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
