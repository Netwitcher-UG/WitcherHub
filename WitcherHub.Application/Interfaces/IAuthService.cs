namespace WitcherHub.Application.Interfaces
{
    public sealed record LoginRequest(string Email, string Password);
    public sealed record AuthResponse(string AccessToken, DateTime ExpiresAtUtc);

    /// <summary>
    /// Result of applying a new password. <see cref="Errors"/> carries the
    /// validation messages to show the user (expired link, password too short).
    /// </summary>
    public sealed record PasswordResetResult(bool Succeeded, IReadOnlyList<string> Errors)
    {
        public static PasswordResetResult Success() => new(true, []);
        public static PasswordResetResult Failure(params string[] errors) => new(false, errors);
    }

    public interface IAuthService
    {
        Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

        /// <summary>
        /// Emails a password reset link if the address belongs to a user.
        ///
        /// Deliberately reports nothing back about whether the address exists —
        /// callers must show the same message either way, so this endpoint cannot
        /// be used to discover which addresses have accounts.
        /// </summary>
        Task RequestPasswordResetAsync(string email, CancellationToken ct = default);

        /// <summary>
        /// Applies a new password using a token from a reset link.
        /// </summary>
        Task<PasswordResetResult> ResetPasswordAsync(
            string email,
            string encodedToken,
            string newPassword,
            CancellationToken ct = default);
    }
}
