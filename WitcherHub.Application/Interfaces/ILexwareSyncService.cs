namespace WitcherHub.Application.Interfaces
{
    public interface ILexwareSyncService
    {
        Task<LexwareImportResult> ImportAllContactsAsync(CancellationToken ct = default);
    }

    public sealed class LexwareImportResult
    {
        public int TotalFromLexware { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; set; } = new();
        public int Skipped { get; set; }
    }
}
