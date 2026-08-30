using System.Linq;

namespace WitcherHub.Tests
{
    /// <summary>
    /// No assistant action waits on the model while a browser request is open.
    ///
    /// Reported as "the server did not answer in time (HTTP 502) … the AI buttons
    /// not working. I become this error every time I click on any AI buttons."
    ///
    /// A model call over a real contract takes minutes. Held on the request that
    /// asked for it, it outlives what the platform proxy allows: the proxy gives
    /// up, the browser is shown 502, and the work carries on into a request
    /// nobody is listening to. Reading a supplied document was moved off the
    /// request thread the first time this happened. Writing the contract and
    /// tidying the positions were left on it — the two buttons that were still
    /// failing — and writing the contract then became several model calls instead
    /// of one, which turned "sometimes too slow" into "always too slow".
    ///
    /// These are shape tests over the handlers and the page script rather than
    /// timing tests. What matters is that no code path calls the model from a
    /// request handler, and that is a property of the code, not of a stopwatch.
    /// </summary>
    public class AiActionsDoNotHoldTheRequestTests
    {
        private static string Page() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "Pages", "Contracts", "Positions.cshtml.cs"));

        private static string Script() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "wwwroot", "js", "pages", "contracts", "positions-builder.js"));

        private static string JobService() => File.ReadAllText(Path.Combine(
            TestPaths.Repository, "WitcherHub.Infrastructure", "Services", "Contracts",
            "ContractAiJobService.cs"));

        private static string OverridePage() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "Pages", "Contracts", "Override.cshtml.cs"));

        private static string OverrideMarkup() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "Pages", "Contracts", "Override.cshtml"));

        private static string OverrideGenerator() => File.ReadAllText(Path.Combine(
            TestPaths.Repository, "WitcherHub.Infrastructure", "Services", "Contracts",
            "ContractOverrideGenerator.cs"));

        // ================================================ nothing waits inline

        [Fact]
        public void NoRequestHandlerCallsTheModel()
        {
            var page = Page();

            // The two that did. Both went through the model and both answered 502
            // for it; the third, the analysis, was moved long ago.
            Assert.DoesNotContain("_organizer.OrganizeAsync(", page);
            Assert.DoesNotContain("_drafts.GenerateAsync(", page);

            // Both are queued instead.
            Assert.Contains("_jobs.StartAsync(", page);
        }

        [Fact]
        public void EveryLongActionIsStartedAndThenPolled()
        {
            var page = Page();

            foreach (var kind in new[] { "ContractAiJobKind.Generation", "ContractAiJobKind.Organize" })
                Assert.Contains(kind, page);

            // One status handler for all of them, rather than one per action.
            Assert.Contains("OnPostAiJobStatusAsync", page);

            var script = Script();

            Assert.Contains("runJob(\"GenerateDraft\"", script);
            Assert.Contains("runJob(\"Organize\"", script);
            Assert.Contains("post(\"AiJobStatus\"", script);
        }

        [Fact]
        public void ThePageNoLongerExpectsAnAnswerFromTheStartingCall()
        {
            var script = Script();

            // `await post("Organize", …)` returning the finished proposal is what
            // the 502 replaced. The start returns a job id; the answer comes from
            // polling.
            Assert.DoesNotContain("await post(\"Organize\"", script);
            Assert.DoesNotContain("await post(\"GenerateDraft\"", script);
        }

        [Fact]
        public void TheWaitingIsWrittenOnceAndUsedByAllThree()
        {
            var script = Script();

            // Three copies of a polling loop is three places for the back-off, the
            // give-up and the session handling to drift apart.
            Assert.Equal(1, Occurrences(script, "async function waitFor("));

            Assert.Contains("runAnalysis", script);
            Assert.Contains("waitFor(", script);
        }

        [Fact]
        public void TheWaitAllowsForAContractThatTakesSeveralCalls()
        {
            var script = Script();

            // Writing a contract is a plan, several batches of sections and an
            // audit now. Five minutes was sized for one reading of one document.
            Assert.Contains("JOB_GIVE_UP_MS = 10 * 60 * 1000", script);
            Assert.DoesNotContain("ANALYSIS_GIVE_UP_MS", script);
        }

        // ================================================== the job's own care

        [Fact]
        public void TheWorkDoesNotHoldAnythingBelongingToTheRequest()
        {
            var job = JobService();

            // The request's scope is disposed the moment it answers — minutes
            // before the work finishes. A captured context would throw then, in a
            // place with nobody to tell.
            // A scope of its own, disposed asynchronously because UnitOfWork is
            // IAsyncDisposable-only.
            Assert.Contains("_scopes.CreateAsyncScope()", job);
            Assert.Contains("services.GetRequiredService<AppDbContext>()", job);

            // And the request's cancellation token would cancel the job the
            // instant the page was answered.
            Assert.Contains("Deliberately not the caller's cancellation token", job);
            Assert.Contains("QueueAsync(async _ => await RunAsync(job.Id), ct)", job);
        }

        [Fact]
        public void ASecondPressJoinsTheRunAlreadyGoing()
        {
            var job = JobService();

            // Pressing twice costs one run, not two — and, for generation, one
            // version rather than two.
            Assert.Contains("ContractAiJobHandle.Joined", job);
            Assert.Contains("RequestKey", job);
            Assert.Contains("IdempotencyKey = job.RequestKey", job);
        }

        [Fact]
        public void AJobLostToARestartDoesNotJamTheButtonForEver()
        {
            var job = JobService();

            // The queue is in-process. A restart loses what it was holding, and a
            // row still saying "running" would refuse every future press.
            Assert.Contains("HasBeenAbandoned", job);
            Assert.Contains("ContractAiJobStatus.Failed", job);

            var entity = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Infrastructure", "Data", "Models", "ContractAiJob.cs"));

            Assert.Contains("AbandonedAfter", entity);
        }

        [Fact]
        public void AnUnexpectedFaultIsWrittenToTheRowRatherThanThrownIntoNothing()
        {
            var job = JobService();

            // There is no request to return an error to. A job that only threw
            // would leave the page polling a status that never changed.
            Assert.Contains("RecordUnexpectedFailureAsync", job);

            // Written with a clean context: the one that threw may be in no state
            // to save.
            var recover = Between(job, "private async Task RecordUnexpectedFailureAsync", "private static void Record(");
            Assert.Contains("_scopes.CreateAsyncScope()", recover);
        }

        [Fact]
        public void AFailureThatWillNotFixItselfIsNotOfferedAsARetry()
        {
            var job = JobService();

            // Telling somebody to try again when the API key is missing is what
            // leaves them pressing a button that cannot succeed. The work's own
            // judgement is stored and returned.
            Assert.Contains("ErrorIsTransient", job);
            Assert.Contains("job.ErrorIsTransient != false", job);
        }

        [Fact]
        public void NothingAboutTheJobLeaksIntoTheUsersMessage()
        {
            var job = JobService();

            // These messages go straight to the screen. No stack traces, no
            // provider responses, no ids the user cannot act on.
            foreach (var leak in new[] { "ex.ToString()", "ex.StackTrace", "ex.Message}" })
                Assert.DoesNotContain(leak, job);
        }

        [Fact]
        public void TheQueuedWorkIsStoredBecauseTheRequestIsGone()
        {
            var entity = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Infrastructure", "Data", "Models", "ContractAiJob.cs"));

            // The positions to tidy and the instructions to write with arrive on a
            // request that ends immediately. There is nowhere else for them to
            // live until the work runs.
            Assert.Contains("public JsonDocument? Request", entity);
            Assert.Contains("public JsonDocument? Result", entity);
            Assert.Contains("jsonb", entity);
        }

        // ============================================== what the user is told

        [Fact]
        public void TheContractComposedWithoutTheAssistantSaysSo()
        {
            var script = Script();

            // It succeeds — the work must not stop because OpenAI is down — but it
            // is plainer than a written one, and the old handler built its own
            // success message and dropped the explanation entirely.
            Assert.Contains("result.composedWithoutAi", script);
            Assert.Contains("pending.unshift(result.notice)", script);
        }

        [Fact]
        public void ThePageStillSaysHowLongItHasBeenWorking()
        {
            var script = Script();

            // These take minutes. A button that says nothing for four of them
            // reads as a button that did nothing.
            Assert.Contains("showProgress(button", script);
            Assert.Contains("elapsedSeconds", script);
        }

        // ---------------------------------------------------------------

        private static string Between(string text, string from, string to)
        {
            var start = text.IndexOf(from, StringComparison.Ordinal);
            if (start < 0) return "";

            var end = text.IndexOf(to, start, StringComparison.Ordinal);
            return end < 0 ? text[start..] : text[start..end];
        }

        private static int Occurrences(string text, string needle)
        {
            var count = 0;
            var at = 0;

            while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }

            return count;
        }
        // ============================================ the override screen too

        /// <summary>
        /// The override screen was the last one calling the model on the request
        /// that asked for it — a plain form POST that ran a full generation before
        /// answering, which is the same 502 with a different button on it.
        /// </summary>
        [Fact]
        public void TheOverrideScreenDoesNotGenerateOnTheRequest()
        {
            var page = OverridePage();

            // What it used to do, inline, before answering.
            Assert.DoesNotContain("_contractDocumentGenerator.GenerateAsync(", page);
            Assert.DoesNotContain("_contractDocumentGenerator", page);

            // What it does now.
            Assert.Contains("_jobs.StartAsync(", page);
            Assert.Contains("ContractAiJobKind.Override", page);
            Assert.Contains("OnPostAiJobStatusAsync", page);
        }

        [Fact]
        public void TheOverrideJobIsDispatchedToRealWork()
        {
            var service = JobService();

            // A kind with no branch would fall to the default and fail every press
            // with "that kind of request is not supported".
            Assert.Contains("ContractAiJobKind.Override => await OverrideAsync(", service);
            Assert.Contains("IContractOverrideGenerator", service);
        }

        /// <summary>
        /// The generation is re-checked against the record when it runs, not only
        /// when it was asked for: a contract can be signed in the minutes a job
        /// spends in the queue, and rewriting a signed contract cannot be undone.
        /// </summary>
        [Fact]
        public void AContractSignedWhileTheJobWaitedIsNotRewritten()
        {
            var generator = OverrideGenerator();

            Assert.Contains("IsLocked(contract)", generator);
            Assert.Contains("has been signed", generator);
        }

        /// <summary>
        /// Whatever the model does, the provider's own words never reach the
        /// screen: the failure is reported through the classified UserMessage,
        /// which carries the cause and a reference but never the key, the prompt
        /// or the raw response.
        /// </summary>
        [Fact]
        public void AnOverrideFailureIsReportedWithoutProviderDetail()
        {
            var generator = OverrideGenerator();

            Assert.Contains("catch (AiInvocationException", generator);
            Assert.Contains("ex.UserMessage", generator);

            // The raw exception must not be what the user is shown: its message
            // quotes the provider's response back, and its ToString carries the
            // stack trace with it.
            Assert.DoesNotContain("ex.Message", generator);
            Assert.DoesNotContain("ex.ToString()", generator);
            Assert.DoesNotContain("StackTrace", generator);
            Assert.DoesNotContain("GetBaseException", generator);
        }

        // ============================================ the override page script

        [Fact]
        public void TheOverrideButtonAlwaysComesBack()
        {
            var markup = OverrideMarkup();

            // Every exit from the work runs through one release, and it is in a
            // finally block so no failure can skip it.
            Assert.Contains("function unlock()", markup);
            Assert.Contains("finally {", markup);
            Assert.Contains("unlock();", markup);

            // The back/forward cache restores the page exactly as it was left,
            // disabled button included, with no request in flight to release it.
            Assert.Contains("pageshow", markup);
        }

        [Fact]
        public void TheOverrideScriptCannotWaitForEver()
        {
            var markup = OverrideMarkup();

            // A request with no deadline never settles, so the finally block that
            // releases the button is never reached.
            Assert.Contains("AbortController", markup);
            Assert.Contains("REQUEST_TIMEOUT_MS", markup);

            // And the polling itself ends rather than asking for ever.
            Assert.Contains("GIVE_UP_MS", markup);
            Assert.Contains("while (waited < GIVE_UP_MS)", markup);
        }

        /// <summary>
        /// Every background scope is disposed asynchronously.
        ///
        /// UnitOfWork implements only IAsyncDisposable, and disposing a scope that
        /// holds one with a plain `using` throws — but only after the work has
        /// finished and been recorded. So the user saw the right answer while
        /// every assistant job and every analysis ended by throwing on the way
        /// out: the queue logged "background work item failed" for work that had
        /// succeeded, and the scope's connection was never released cleanly.
        /// </summary>
        [Theory]
        [InlineData("WitcherHub.Infrastructure", "Services", "Contracts", "ContractAiJobService.cs")]
        [InlineData("WitcherHub.Infrastructure", "Services", "Contracts", "BackgroundAnalysisRunner.cs")]
        [InlineData("WitcherHub.Infrastructure", "Services", "Lexware", "RecurringInvoiceHostedService.cs")]
        public void BackgroundWorkDisposesItsScopeAsynchronously(params string[] parts)
        {
            var source = File.ReadAllText(
                Path.Combine(new[] { TestPaths.Repository }.Concat(parts).ToArray()));

            Assert.DoesNotContain("CreateScope()", source);
            Assert.Contains("CreateAsyncScope()", source);
        }

        [Fact]
        public void TheSameGenerationIsNeverSentTwice()
        {
            var markup = OverrideMarkup();
            var page = OverridePage();

            // In flight, a second press does nothing at all.
            Assert.Contains("if (busy) return;", markup);

            // And a retry that does reach the server joins the running job rather
            // than writing a second contract.
            Assert.Contains("IdempotencyKey", markup);
            Assert.Contains("requestKey: Vm.IdempotencyKey", page);
        }
    }
}
