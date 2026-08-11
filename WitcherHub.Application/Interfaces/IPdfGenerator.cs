namespace WitcherHub.Application.Interfaces
{
    public interface IPdfGenerator
    {
        /// <summary>
        /// Blocks the calling thread while Chromium renders. Kept for existing
        /// callers; prefer <see cref="FromHtmlAsync"/> in request handlers.
        /// </summary>
        byte[] FromHtml(string html, string? documentTitle = null);

        Task<byte[]> FromHtmlAsync(
            string html,
            string? documentTitle = null,
            CancellationToken ct = default);
    }
}
