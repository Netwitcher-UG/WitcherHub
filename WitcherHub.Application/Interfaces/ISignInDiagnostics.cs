namespace WitcherHub.Application.Interfaces
{
    /// <summary>
    /// One line of a sign-in diagnostic report: a label and a value, ready to be
    /// rendered in a table or copied into a message.
    /// </summary>
    public sealed record SignInDiagnosticFact(string Label, string Value);

    /// <summary>
    /// Everything an administrator needs to explain a failed sign-in, gathered
    /// after the attempt failed.
    ///
    /// Deliberately contains no passwords, hashes, tokens, connection strings or
    /// stack frames — only the facts that distinguish "wrong password" from
    /// "wrong database", which is the distinction that has cost the most time.
    /// </summary>
    public sealed record SignInDiagnosticsReport(IReadOnlyList<SignInDiagnosticFact> Facts)
    {
        public static SignInDiagnosticsReport Empty { get; } = new([]);

        /// <summary>Plain text, so the whole report can be copied in one click.</summary>
        public string ToPlainText() =>
            string.Join(Environment.NewLine, Facts.Select(f => $"{f.Label}: {f.Value}"));
    }

    /// <summary>
    /// Describes the environment a sign-in attempt ran against.
    ///
    /// Exists because the login page can only say "check email/password", which is
    /// the wrong advice whenever the real cause is that the instance is talking to
    /// a different database than the one holding the account.
    /// </summary>
    public interface ISignInDiagnostics
    {
        /// <summary>
        /// True when the running configuration allows the detailed report to be
        /// rendered. Off unless <c>Auth__ShowSignInDiagnostics</c> says otherwise,
        /// and on by default in Development.
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Collects the facts about <paramref name="email"/> and this instance.
        /// Never throws: a diagnostic that fails must not replace the failure it
        /// was called to explain.
        /// </summary>
        Task<SignInDiagnosticsReport> DescribeAsync(string email, CancellationToken ct = default);
    }
}
