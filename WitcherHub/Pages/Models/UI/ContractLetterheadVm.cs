using Microsoft.Extensions.Options;
using WitcherHub.Infrastructure.Services.Pdf;

namespace WitcherHub.Pages.Models.UI
{
    /// <summary>
    /// The marks at the top of a contract sheet: the company's logo, the number
    /// the document is cited by, and the date it was issued.
    ///
    /// The logo path is a setting rather than a literal, because it was a literal
    /// and it was wrong — the PDF generator looked for the file under a directory
    /// this repository does not have, logged a warning nobody read, and rendered
    /// an empty image. Contracts went out unbranded and it looked deliberate.
    /// </summary>
    public sealed class ContractLetterheadVm
    {
        /// <summary>Empty when no logo is configured or the file is not there.</summary>
        public string? LogoUrl { get; init; }

        public string CompanyName { get; init; } = "";

        /// <summary>From the contract record, never composed or guessed.</summary>
        public string? ContractNo { get; init; }

        public string? IssuedOn { get; init; }

        /// <summary>
        /// Builds the letterhead, checking that the configured file actually
        /// exists before pointing a page at it.
        /// </summary>
        /// <param name="webRootPath">The application's wwwroot, for the existence check.</param>
        /// <param name="pathBase">The request's path base, so the URL is right behind a prefix.</param>
        public static ContractLetterheadVm Build(
            IOptions<BrandingOptions> branding,
            string webRootPath,
            string pathBase,
            string companyName,
            string? contractNo,
            DateTimeOffset? issuedOn)
        {
            var relative = (branding.Value.LogoPath ?? "").Replace('\\', '/').Trim('/');

            var exists = relative.Length > 0 &&
                         File.Exists(Path.Combine(
                             new[] { webRootPath }.Concat(relative.Split('/')).ToArray()));

            return new ContractLetterheadVm
            {
                LogoUrl = exists ? $"{pathBase.TrimEnd('/')}/{relative}" : null,
                CompanyName = companyName,
                ContractNo = contractNo,
                IssuedOn = issuedOn is { } when_ ? Format.Date(when_) : null
            };
        }
    }
}
