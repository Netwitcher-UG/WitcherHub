using System.ComponentModel.DataAnnotations;
using WitcherHub.Domain.Commen;

namespace WitcherHub.Infrastructure.Data.Models
{
    /// <summary>
    /// An approved contract, rendered in another language for the customer to
    /// read before they sign the German one.
    ///
    /// Cached because it is expensive and because it must not change under the
    /// reader. Translating on every page view would cost a model call each time
    /// a customer scrolled back, and — worse — two visits could show two
    /// different wordings of the same clause, which is precisely the kind of
    /// thing somebody notices after they have signed.
    ///
    /// Keyed by the fingerprint of the German it was made from, so it is not a
    /// cache that can go stale: a contract whose wording changes no longer
    /// matches its stored translation and a new one is produced. Nothing here is
    /// ever signed, stored as the contract, or put in the PDF.
    /// </summary>
    public class ContractTermsTranslation : BaseEntity
    {
        public Guid ContractId { get; set; }
        public Contract Contract { get; set; } = default!;

        /// <summary>Two-letter code of the language this is in — "en".</summary>
        [MaxLength(8)]
        public string Language { get; set; } = default!;

        /// <summary>
        /// SHA-256 of the German wording this was translated from. The reason
        /// this row can never be served for a contract that has since changed.
        /// </summary>
        [MaxLength(64)]
        public string SourceHash { get; set; } = default!;

        public string TranslatedMarkdown { get; set; } = default!;

        /// <summary>
        /// Which instructions produced it, so a translation made under older
        /// rules can be told apart from a current one.
        /// </summary>
        [MaxLength(60)]
        public string? PromptVersion { get; set; }

        public DateTimeOffset TranslatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
