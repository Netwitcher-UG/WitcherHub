namespace WitcherHub.Application.Services.Contracts.Language
{
    /// <summary>What a reading-aid translation came to.</summary>
    public sealed record ContractViewTranslation
    {
        public required bool Succeeded { get; init; }

        /// <summary>The contract in the requested language, structure intact.</summary>
        public string? Markdown { get; init; }

        /// <summary>Why it failed. Shown as "translation unavailable", never as a stack trace.</summary>
        public string? FailureReason { get; init; }

        /// <summary>Passages the model could not translate without guessing.</summary>
        public IReadOnlyList<string> ReviewFlags { get; init; } = [];

        public static ContractViewTranslation Failed(string reason) =>
            new() { Succeeded = false, FailureReason = reason };
    }

    /// <summary>
    /// Renders an approved German contract in another language, for reading.
    ///
    /// Never for signing. The German wording is the agreement, it is what is
    /// stored, and it is what the signed PDF contains — a customer who signs
    /// after reading this has signed the German text, and the page says so.
    /// Producing a signable translation would mean two documents that can
    /// disagree, and a dispute about which one was agreed.
    /// </summary>
    public interface IContractViewTranslator
    {
        /// <summary>
        /// The contract in <paramref name="language"/>, or a failure.
        ///
        /// Falls back to nothing: a partial or unverifiable translation is
        /// refused, and the caller shows the German. A customer reading half a
        /// translation does not know which half.
        /// </summary>
        Task<ContractViewTranslation> TranslateAsync(
            string markdown,
            string language,
            IReadOnlyList<ProtectedValue> protectedValues,
            CancellationToken ct = default);
    }
}
