namespace WitcherHub.Application.Interfaces
{
    public interface IPdfGenerator
    {
        byte[] FromHtml(string html, string? documentTitle = null);
    }
}
