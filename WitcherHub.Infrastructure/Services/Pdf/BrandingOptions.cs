namespace WitcherHub.Infrastructure.Services.Pdf
{
    /// <summary>
    /// The marks that make a document this company's document.
    ///
    /// The logo used to be a literal path inside the PDF generator, pointing at
    /// <c>wwwroot/theme/assets/images/netwitcher-logo.png</c> — a directory that
    /// does not exist in this repository. The only logo here is at
    /// <c>wwwroot/img/netwitcher-logo.png</c>, which the Razor pages use, so a
    /// contract shown on screen carried the logo and the same contract archived as
    /// a PDF did not. The mismatch was invisible: the generator logged a warning
    /// and substituted an empty src, which renders as nothing at all.
    ///
    /// The path is a setting now, it has one default that is a file that exists,
    /// and start-up says so when it does not.
    /// </summary>
    public class BrandingOptions
    {
        public const string SectionName = "Branding";

        /// <summary>
        /// Where the logo lives, relative to wwwroot. Forward slashes; combined
        /// with the web root rather than concatenated, so it works on any platform.
        /// </summary>
        public string LogoPath { get; set; } = "img/netwitcher-logo.png";

        public const string LogoPathSettingName = "Branding__LogoPath";

        /// <summary>
        /// How tall the logo is set in a document, in millimetres. A logo whose
        /// size is decided by its pixel dimensions changes the look of every
        /// contract the moment somebody replaces the file.
        /// </summary>
        public double LogoHeightMm { get; set; } = 14;
    }
}
