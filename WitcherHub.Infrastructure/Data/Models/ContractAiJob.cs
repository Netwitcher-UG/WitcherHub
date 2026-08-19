using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using WitcherHub.Domain.Commen;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Data.Models
{
    /// <summary>
    /// One long-running assistant action, and where it has got to.
    ///
    /// The model calls that write a contract or tidy a set of positions take
    /// minutes. Run on the HTTP request that asked for them, they outlive what a
    /// platform proxy will hold open, so the browser is shown "HTTP 502 — the
    /// request took too long" while the work is still going, and the answer
    /// arrives into a request nobody is listening to. Reading a supplied document
    /// was moved off the request thread for exactly this reason; these two were
    /// left on it.
    ///
    /// A row here is the channel back to the page: the work writes its outcome
    /// to it, and the page asks this rather than holding a connection. It is also
    /// what makes a second click join the run already going instead of paying for
    /// another one.
    ///
    /// The request and the result are stored because the work outlives the
    /// request that carried them — there is nowhere else for them to live.
    /// </summary>
    public class ContractAiJob : BaseEntity
    {
        public Guid ContractId { get; set; }
        public Contract Contract { get; set; } = default!;

        public ContractAiJobKind Kind { get; set; }

        public ContractAiJobStatus Status { get; set; } = ContractAiJobStatus.Running;

        /// <summary>
        /// The key the browser sent with the request.
        ///
        /// A retry after a timeout carries the same key, so it finds the job the
        /// first attempt started rather than beginning a second one.
        /// </summary>
        [MaxLength(64)]
        public string? RequestKey { get; set; }

        public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? FinishedAt { get; set; }

        /// <summary>What was asked for. Needed because the asking request is long gone.</summary>
        [Column(TypeName = "jsonb")]
        public JsonDocument? Request { get; set; }

        /// <summary>What came back, in the shape the page expects to read.</summary>
        [Column(TypeName = "jsonb")]
        public JsonDocument? Result { get; set; }

        /// <summary>Why it failed, in the words the user should read. Never a stack trace.</summary>
        public string? Error { get; set; }

        /// <summary>True when trying again is worth offering.</summary>
        public bool? ErrorIsTransient { get; set; }

        /// <summary>
        /// How long a job may say it is running before we stop believing it.
        ///
        /// The queue is in-process, so a restart loses whatever was in flight and
        /// would otherwise leave a row saying "running" for ever — and, with it, a
        /// button that refuses to start because something is already going.
        /// </summary>
        public static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(20);

        public bool HasBeenAbandoned =>
            Status == ContractAiJobStatus.Running &&
            DateTimeOffset.UtcNow - StartedAt > AbandonedAfter;
    }
}
