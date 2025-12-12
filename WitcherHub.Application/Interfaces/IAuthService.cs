
namespace WitcherHub.Application.Interfaces
{
    public sealed record LoginRequest(string Email, string Password);
    public sealed record AuthResponse(string AccessToken, DateTime ExpiresAtUtc);

    public interface IAuthService
    {
        Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    }
}
