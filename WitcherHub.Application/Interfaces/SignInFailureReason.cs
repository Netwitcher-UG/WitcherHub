namespace WitcherHub.Application.Interfaces
{
    /// <summary>
    /// Why a sign-in attempt did not produce a token.
    ///
    /// The login page shows the same sentence to every visitor, so this never
    /// reaches an anonymous browser as prose. It reaches it as a short code, which
    /// tells an administrator exactly which of these happened without telling a
    /// stranger whether an address has an account.
    /// </summary>
    public enum SignInFailureReason
    {
        /// <summary>Something failed that is not a credential problem.</summary>
        Unknown = 0,

        /// <summary>
        /// The database this instance is connected to has no user accounts at all.
        /// Almost always means the instance points at the wrong database, or that
        /// seeding never ran.
        /// </summary>
        NoAccountsExist = 1,

        /// <summary>
        /// Accounts exist, but none with this address. Usually the wrong
        /// environment, or a typo.
        /// </summary>
        UnknownEmail = 2,

        /// <summary>The account exists and the password did not match.</summary>
        IncorrectPassword = 3,

        /// <summary>The account exists and is locked out.</summary>
        AccountLockedOut = 4,

        /// <summary>The account exists but has no password hash stored at all.</summary>
        NoPasswordSet = 5
    }

    public static class SignInFailureReasonExtensions
    {
        /// <summary>
        /// The code printed on the login page. Stable, so it can be quoted in a
        /// bug report and matched against the log.
        /// </summary>
        public static string ToCode(this SignInFailureReason reason) => reason switch
        {
            SignInFailureReason.NoAccountsExist => "AUTH-01",
            SignInFailureReason.UnknownEmail => "AUTH-02",
            SignInFailureReason.IncorrectPassword => "AUTH-03",
            SignInFailureReason.AccountLockedOut => "AUTH-04",
            SignInFailureReason.NoPasswordSet => "AUTH-05",
            _ => "AUTH-00"
        };

        /// <summary>
        /// What an administrator should read. Shown only where sign-in diagnostics
        /// are switched on, because it distinguishes "no such account" from "wrong
        /// password" and so could be used to discover which addresses exist.
        /// </summary>
        public static string ToAdministratorExplanation(this SignInFailureReason reason) => reason switch
        {
            SignInFailureReason.NoAccountsExist =>
                "The database this site is connected to contains no user accounts at all. " +
                "Either the connection string points at the wrong database, or start-up seeding never ran.",

            SignInFailureReason.UnknownEmail =>
                "No account with this email address exists in the database this site is connected to. " +
                "The account most likely lives in the other environment's database.",

            SignInFailureReason.IncorrectPassword =>
                "The account exists here, but the password did not match the stored hash.",

            SignInFailureReason.AccountLockedOut =>
                "The account exists and is currently locked out.",

            SignInFailureReason.NoPasswordSet =>
                "The account exists but has no password stored, so no password can match. " +
                "Use 'Forgot password?' to set one.",

            _ => "Sign-in failed for a reason that is not a credential problem. See the details below."
        };
    }
}
