namespace WitcherHub.Application.Interfaces
{
    /// <summary>
    /// Runs one contract analysis away from the request that asked for it.
    ///
    /// Reading a full contract takes longer than a platform proxy will hold a
    /// connection open — the browser was shown HTTP 502 while the model was
    /// still working, and the reading then completed into a request nobody was
    /// listening to. The work is the same; only who waits for it changes.
    ///
    /// This is an interface rather than a direct call into the queue so the
    /// service can be built without one — in tests, and anywhere the reading
    /// should simply happen inline.
    /// </summary>
    public interface IBackgroundAnalysisRunner
    {
        /// <summary>
        /// Runs the analysis of one draft version, in its own scope, and returns
        /// as soon as it is queued.
        ///
        /// The work outlives the request, so it must not capture anything scoped
        /// to it: the implementation resolves its own services and its own
        /// database context.
        /// </summary>
        ValueTask RunAsync(Guid contractId, int version);
    }
}
