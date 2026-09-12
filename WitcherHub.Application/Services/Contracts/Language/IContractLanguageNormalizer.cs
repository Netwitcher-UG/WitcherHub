namespace WitcherHub.Application.Services.Contracts.Language
{
    /// <summary>
    /// Puts the descriptive content of a contract into German.
    ///
    /// One responsibility, and a narrow one. It is given text and protected
    /// values and returns German text; it knows nothing about contracts,
    /// positions, prices or clauses, and it decides nothing about them. That
    /// narrowness is what makes it safe to hand customer wording to a model: the
    /// worst a bad answer can do is fail a deterministic check and stop the
    /// contract, rather than change a term of it.
    /// </summary>
    public interface IContractLanguageNormalizer
    {
        /// <summary>
        /// Translates every field of the request into German.
        ///
        /// Succeeds only when every requested field came back, every protected
        /// value survived, and no deterministic check found a change of meaning.
        /// A partial answer is a failure: a contract missing one scope paragraph
        /// still reads as a finished contract, which is the reason not to return
        /// one.
        /// </summary>
        Task<ContractNormalizationResult> NormalizeAsync(
            ContractNormalizationRequest request, CancellationToken ct = default);
    }
}
