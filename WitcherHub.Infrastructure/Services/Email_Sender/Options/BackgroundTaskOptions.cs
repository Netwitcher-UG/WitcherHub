namespace WitcherHub.Infrastructure.Services.Email_Sender.Options
{
    public sealed class BackgroundTaskOptions
    {
        public int Capacity { get; init; } = 200;

        /// <summary>
        /// How many queued jobs may run at once.
        ///
        /// This was 1, and the worker that read it was not the one registered:
        /// the registered worker awaited each item in a single loop with no
        /// option at all. Everything the application does off the request thread
        /// shares that one queue — reading a contract, writing one, tidying
        /// positions, sending mail — so one long job stopped all of them.
        ///
        /// It stopped being theoretical when writing a contract became several
        /// model calls: press Generate, then press Analyse, and the analysis did
        /// not start at all until the generation had finished. What the user saw
        /// was "the contract is taking unusually long to read" about a reading
        /// that had not begun.
        ///
        /// Four is enough that no ordinary action waits on another and small
        /// enough that a burst cannot open an unbounded number of model calls at
        /// once.
        /// </summary>
        public int MaxConcurrency { get; init; } = 4;
    }
}
