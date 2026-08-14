using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Pages.Auth;

namespace WitcherHub.Tests;

/// <summary>
/// What the login page tells a person when sign-in fails.
///
/// "Login failed. Check email/password." was the only answer the page ever gave,
/// for every cause: an account in the other environment's database, an account
/// with no password hash, a locked-out account, a JWT key the token handler
/// rejected. These tests pin down that the page now carries a code and a
/// reference for each of those, that the prose stays identical either way, and
/// that the detail only appears where it has been switched on.
/// </summary>
public class SignInFailureReportingTests
{
    private sealed class ThrowingAuthService : IAuthService
    {
        private readonly Exception _exception;

        public ThrowingAuthService(Exception exception) => _exception = exception;

        public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
            throw _exception;

        public Task RequestPasswordResetAsync(string email, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<PasswordResetResult> ResetPasswordAsync(
            string email, string encodedToken, string newPassword, CancellationToken ct = default) =>
            Task.FromResult(PasswordResetResult.Success());
    }

    private sealed class StubDiagnostics : ISignInDiagnostics
    {
        private readonly SignInDiagnosticsReport _report;

        public StubDiagnostics(bool enabled, params SignInDiagnosticFact[] facts)
        {
            IsEnabled = enabled;
            _report = new SignInDiagnosticsReport(facts);
        }

        public bool IsEnabled { get; }

        public Task<SignInDiagnosticsReport> DescribeAsync(string email, CancellationToken ct = default) =>
            Task.FromResult(_report);
    }

    private static LoginModel BuildModel(Exception failure, ISignInDiagnostics diagnostics) =>
        new(new ThrowingAuthService(failure), diagnostics, NullLogger<LoginModel>.Instance)
        {
            Email = "someone@example.com",
            Password = "whatever"
        };

    [Theory]
    [InlineData(SignInFailureReason.NoAccountsExist, "AUTH-01")]
    [InlineData(SignInFailureReason.UnknownEmail, "AUTH-02")]
    [InlineData(SignInFailureReason.IncorrectPassword, "AUTH-03")]
    [InlineData(SignInFailureReason.AccountLockedOut, "AUTH-04")]
    [InlineData(SignInFailureReason.NoPasswordSet, "AUTH-05")]
    [InlineData(SignInFailureReason.Unknown, "AUTH-00")]
    public void Each_reason_has_a_stable_code(SignInFailureReason reason, string expected)
    {
        // These codes get quoted in bug reports and matched against log lines, so
        // they must not drift when the enum is reordered or extended.
        Assert.Equal(expected, reason.ToCode());
    }

    [Fact]
    public void Every_reason_explains_itself()
    {
        foreach (var reason in Enum.GetValues<SignInFailureReason>())
        {
            var explanation = reason.ToAdministratorExplanation();

            Assert.False(string.IsNullOrWhiteSpace(explanation));
            Assert.EndsWith(".", explanation.TrimEnd());
        }
    }

    [Fact]
    public async Task Credential_failure_shows_the_code_and_a_reference()
    {
        var model = BuildModel(
            new AuthenticationFailedAppException(SignInFailureReason.UnknownEmail),
            new StubDiagnostics(enabled: false));

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("AUTH-02", model.FailureCode);
        Assert.False(string.IsNullOrWhiteSpace(model.Reference));
        Assert.NotNull(model.FailedAtUtc);
    }

    [Fact]
    public async Task Credential_failure_says_nothing_about_which_check_failed()
    {
        // The page is anonymous, so the sentence a stranger reads must be the same
        // whether or not the address has an account here.
        var unknownEmail = BuildModel(
            new AuthenticationFailedAppException(SignInFailureReason.UnknownEmail),
            new StubDiagnostics(enabled: false));

        var wrongPassword = BuildModel(
            new AuthenticationFailedAppException(SignInFailureReason.IncorrectPassword),
            new StubDiagnostics(enabled: false));

        await unknownEmail.OnPostAsync(CancellationToken.None);
        await wrongPassword.OnPostAsync(CancellationToken.None);

        Assert.Equal(unknownEmail.ErrorMessage, wrongPassword.ErrorMessage);
        Assert.NotEqual(unknownEmail.FailureCode, wrongPassword.FailureCode);
    }

    [Fact]
    public async Task Diagnostics_stay_hidden_until_they_are_switched_on()
    {
        var model = BuildModel(
            new AuthenticationFailedAppException(SignInFailureReason.IncorrectPassword),
            new StubDiagnostics(enabled: false, new SignInDiagnosticFact("Database name", "witcherhub_dev")));

        await model.OnPostAsync(CancellationToken.None);

        Assert.Empty(model.DiagnosticFacts);
        Assert.Null(model.DiagnosticExplanation);
        Assert.False(model.DiagnosticsAvailable);
        Assert.DoesNotContain("witcherhub_dev", model.CopyableReport);
    }

    [Fact]
    public async Task Diagnostics_appear_and_are_copyable_when_switched_on()
    {
        var model = BuildModel(
            new AuthenticationFailedAppException(SignInFailureReason.UnknownEmail),
            new StubDiagnostics(
                enabled: true,
                new SignInDiagnosticFact("Database name", "witcherhub_dev"),
                new SignInDiagnosticFact("Accounts in this database", "3")));

        await model.OnPostAsync(CancellationToken.None);

        Assert.True(model.DiagnosticsAvailable);
        Assert.Equal(2, model.DiagnosticFacts.Count);
        Assert.Equal(
            SignInFailureReason.UnknownEmail.ToAdministratorExplanation(),
            model.DiagnosticExplanation);

        // The copy button hands over one block of text; it has to carry the code,
        // the reference and the facts, or it is not worth clicking.
        Assert.Contains("AUTH-02", model.CopyableReport);
        Assert.Contains(model.Reference!, model.CopyableReport);
        Assert.Contains("witcherhub_dev", model.CopyableReport);
        Assert.Contains("Accounts in this database: 3", model.CopyableReport);
    }

    [Fact]
    public async Task A_system_error_is_reported_as_a_server_problem_not_a_password_problem()
    {
        var model = BuildModel(
            new InvalidOperationException("IDX10653: signing key is too small"),
            new StubDiagnostics(enabled: true));

        await model.OnPostAsync(CancellationToken.None);

        Assert.True(model.IsSystemError);
        Assert.Equal("AUTH-500", model.FailureCode);
        Assert.Contains("server problem", model.ErrorMessage!);

        // The exception text belongs in the report, which is gated, and never in
        // the sentence every anonymous visitor sees.
        Assert.DoesNotContain("IDX10653", model.ErrorMessage!);
        Assert.Contains("IDX10653", model.CopyableReport);
    }

    [Fact]
    public async Task A_system_error_keeps_its_detail_out_of_the_page_when_diagnostics_are_off()
    {
        var model = BuildModel(
            new InvalidOperationException("Npgsql: password authentication failed for user \"app\""),
            new StubDiagnostics(enabled: false));

        await model.OnPostAsync(CancellationToken.None);

        Assert.True(model.IsSystemError);
        Assert.Null(model.DiagnosticExplanation);
        Assert.DoesNotContain("password authentication failed", model.CopyableReport);
    }

    [Fact]
    public async Task An_empty_field_is_answered_without_calling_the_service()
    {
        // Reaching the auth service with a blank password produced a credential
        // failure and a reference code for what is really a form error.
        var model = new LoginModel(
            new ThrowingAuthService(new InvalidOperationException("must not be called")),
            new StubDiagnostics(enabled: true),
            NullLogger<LoginModel>.Instance)
        {
            Email = "someone@example.com",
            Password = "   "
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Enter both an email address and a password.", model.ErrorMessage);
        Assert.Null(model.FailureCode);
    }

    [Fact]
    public async Task References_do_not_repeat()
    {
        // A reference that collides is worse than none: it points at the wrong log
        // line with full confidence.
        var seen = new HashSet<string>();

        for (var i = 0; i < 500; i++)
        {
            var model = BuildModel(
                new AuthenticationFailedAppException(SignInFailureReason.IncorrectPassword),
                new StubDiagnostics(enabled: false));

            await model.OnPostAsync(CancellationToken.None);

            Assert.True(seen.Add(model.Reference!), $"reference {model.Reference} was issued twice");
        }
    }
}
