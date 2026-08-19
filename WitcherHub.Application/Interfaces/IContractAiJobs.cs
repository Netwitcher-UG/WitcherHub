using WitcherHub.Application.Models.DTO.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Interfaces
{
    /// <summary>
    /// What a generation job was asked for.
    ///
    /// Stored with the job because the request that carried it ends immediately —
    /// the work runs minutes later, in a scope that has never seen it.
    /// </summary>
    public sealed class GenerateJobRequest
    {
        public string? AdditionalInstructions { get; set; }
        public string? Language { get; set; }
    }

    /// <summary>What the organizer was asked for, for the same reason.</summary>
    public sealed class OrganizeJobRequest
    {
        public string? RoughInput { get; set; }
        public string? Currency { get; set; }
        public List<ManualPositionDto>? Positions { get; set; }
    }

    /// <summary>
    /// Starting a long assistant action and asking how it is getting on.
    ///
    /// Both of the actions behind this — writing the contract, tidying the
    /// positions — call the model, and a model call over a real contract takes
    /// longer than a platform proxy will hold a connection open. Run on the
    /// request thread they return HTTP 502 while still working, and the answer
    /// lands in a request nobody is listening to.
    ///
    /// Reading a supplied document was moved off the request thread when that
    /// first happened. These two were left behind, and generation then became
    /// several model calls instead of one, which turned an intermittent failure
    /// into a certain one.
    /// </summary>
    public interface IContractAiJobs
    {
        /// <summary>
        /// Starts one, or joins the one already going.
        ///
        /// A second press costs nothing: it finds the running job for this
        /// contract and kind and waits on that. A job left behind by a restart is
        /// treated as abandoned so the button is never permanently stuck.
        /// </summary>
        Task<ContractAiJobHandle> StartAsync(
            Guid contractId,
            ContractAiJobKind kind,
            object request,
            string? requestKey,
            CancellationToken ct = default);

        /// <summary>Where a job has got to, and its result once it has one.</summary>
        Task<ContractAiJobState> GetAsync(Guid jobId, CancellationToken ct = default);
    }

    /// <summary>What happened when a job was asked for.</summary>
    public sealed class ContractAiJobHandle
    {
        /// <summary>The job to poll. Empty only when nothing was started.</summary>
        public Guid JobId { get; init; }

        public bool Running { get; init; }

        /// <summary>
        /// True when this request found one already in flight and joined it. The
        /// page says "already working on it" rather than pretending it started
        /// something.
        /// </summary>
        public bool AlreadyRunning { get; init; }

        /// <summary>Why nothing could be started. Shown as it stands.</summary>
        public string? FailureReason { get; init; }

        public static ContractAiJobHandle Started(Guid id) =>
            new() { JobId = id, Running = true };

        public static ContractAiJobHandle Joined(Guid id) =>
            new() { JobId = id, Running = true, AlreadyRunning = true };

        public static ContractAiJobHandle Refused(string reason) =>
            new() { Running = false, FailureReason = reason };
    }

    /// <summary>Where a job has got to.</summary>
    public sealed class ContractAiJobState
    {
        public ContractAiJobStatus Status { get; init; }

        public bool Running => Status == ContractAiJobStatus.Running;
        public bool Succeeded => Status == ContractAiJobStatus.Succeeded;
        public bool Failed => Status == ContractAiJobStatus.Failed;

        /// <summary>The result, as JSON text, exactly as the work wrote it.</summary>
        public string? ResultJson { get; init; }

        public string? FailureReason { get; init; }

        public bool IsTransientFailure { get; init; }

        /// <summary>How long it has been going, for the button's own progress text.</summary>
        public TimeSpan? Elapsed { get; init; }
    }
}
