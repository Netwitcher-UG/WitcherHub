using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.Email;
using WitcherHub.Application.Services.Email;
using WitcherHub.Domain.SeedData;
using WitcherHub.Infrastructure.Data.Models;


namespace WitcherHub.Infrastructure.Authentication
{
    public sealed class AuthService : IAuthService
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly RoleManager<IdentityRole<Guid>> _roleManager;
        private readonly JwtOptions _jwt;
        private readonly ILogger<AuthService> _logger;
        private readonly IEmailService _email;
        private readonly IConfiguration _configuration;

        public AuthService(
            UserManager<AppUser> userManager,
            RoleManager<IdentityRole<Guid>> roleManager,
            IOptions<JwtOptions> jwtOptions,
            ILogger<AuthService> logger,
            IEmailService email,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _jwt = jwtOptions.Value;
            _logger = logger;
            _email = email;
            _configuration = configuration;
        }

        public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        {
            var email = (request.Email ?? "").Trim();

            var user = await _userManager.FindByEmailAsync(email);

            // The caller receives one indistinguishable sentence either way, but the
            // reason travels on the exception and into the log — without that,
            // "wrong password" and "no such account in this database" look
            // identical while debugging.
            if (user is null)
            {
                var totalAccounts = await _userManager.Users.CountAsync(ct);

                var reason = totalAccounts == 0
                    ? SignInFailureReason.NoAccountsExist
                    : SignInFailureReason.UnknownEmail;

                _logger.LogWarning(
                    "Sign-in failed [{Code}]: no account with email {Email} exists in this database. " +
                    "Total accounts: {UserCount}.",
                    reason.ToCode(), email, totalAccounts);

                throw new AuthenticationFailedAppException(reason);
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                _logger.LogWarning(
                    "Sign-in failed [{Code}]: the account {Email} (id {UserId}) is locked out until {LockoutEnd}.",
                    SignInFailureReason.AccountLockedOut.ToCode(), email, user.Id, user.LockoutEnd);

                throw new AuthenticationFailedAppException(SignInFailureReason.AccountLockedOut);
            }

            // No stored hash is not the same as a wrong password: no password can
            // ever match, so retyping it is wasted effort.
            if (!await _userManager.HasPasswordAsync(user))
            {
                _logger.LogWarning(
                    "Sign-in failed [{Code}]: the account {Email} (id {UserId}) has no password set.",
                    SignInFailureReason.NoPasswordSet.ToCode(), email, user.Id);

                throw new AuthenticationFailedAppException(SignInFailureReason.NoPasswordSet);
            }

            if (!await _userManager.CheckPasswordAsync(user, request.Password))
            {
                _logger.LogWarning(
                    "Sign-in failed [{Code}]: the account {Email} exists (id {UserId}) but the password did not match.",
                    SignInFailureReason.IncorrectPassword.ToCode(), email, user.Id);

                throw new AuthenticationFailedAppException(SignInFailureReason.IncorrectPassword);
            }

            var roles = await _userManager.GetRolesAsync(user);

            var permissionClaims = new List<Claim>();
            foreach (var roleName in roles.Distinct())
            {
                var role = await _roleManager.FindByNameAsync(roleName);
                if (role is null) continue;

                var roleClaims = await _roleManager.GetClaimsAsync(role);
                permissionClaims.AddRange(roleClaims
                    .Where(c => c.Type == AppClaimTypes.Permission));
            }

            permissionClaims = permissionClaims
                .GroupBy(c => c.Value)
                .Select(g => g.First())
                .ToList();

            var now = DateTime.UtcNow;
            var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

            var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

            claims.AddRange(permissionClaims);

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _jwt.Issuer,
                audience: _jwt.Audience,
                claims: claims,
                notBefore: now,
                expires: expires,
                signingCredentials: creds
            );

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            return new AuthResponse(tokenString, expires);
        }

        public async Task RequestPasswordResetAsync(string email, CancellationToken ct = default)
        {
            var normalized = (email ?? "").Trim();
            if (normalized.Length == 0)
                return;

            var user = await _userManager.FindByEmailAsync(normalized);

            if (user is null || string.IsNullOrWhiteSpace(user.Email))
            {
                // No such account. Return quietly: the page shows the same message
                // either way so this cannot be used to enumerate addresses.
                _logger.LogInformation("Password reset requested for an address with no account.");
                return;
            }

            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetUrl = BuildResetUrl(user.Email, PasswordResetTokenEncoder.Encode(resetToken));

            await _email.QueueTemplateAsync(
                templateName: "PasswordReset",
                model: new
                {
                    UserName = string.IsNullOrWhiteSpace(user.UserName) ? user.Email : user.UserName,
                    ActionUrl = resetUrl,
                    ExpiryHours = 2
                },
                to: new EmailAddress(user.Email, user.UserName),
                subject: "WitcherHub — Passwort zurücksetzen",
                ct: ct);

            // The URL embeds the token, so it is never written to the log.
            _logger.LogInformation("Password reset email queued for user {UserId}.", user.Id);
        }

        public async Task<PasswordResetResult> ResetPasswordAsync(
            string email,
            string encodedToken,
            string newPassword,
            CancellationToken ct = default)
        {
            const string InvalidLinkMessage =
                "This reset link is invalid or has expired. Please request a new one.";

            if (!PasswordResetTokenEncoder.TryDecode(encodedToken, out var resetToken))
                return PasswordResetResult.Failure(InvalidLinkMessage);

            var user = await _userManager.FindByEmailAsync((email ?? "").Trim());
            if (user is null)
            {
                // Same message as an expired token: do not confirm the address exists.
                return PasswordResetResult.Failure(InvalidLinkMessage);
            }

            var result = await _userManager.ResetPasswordAsync(user, resetToken, newPassword);

            if (result.Succeeded)
            {
                _logger.LogInformation("Password reset completed for user {UserId}.", user.Id);
                return PasswordResetResult.Success();
            }

            // Identity reports an unusable token as "InvalidToken"; translate that
            // into something a person can act on, and surface password-policy
            // failures as they are.
            var errors = result.Errors
                .Select(e => e.Code == "InvalidToken" ? InvalidLinkMessage : e.Description)
                .Distinct()
                .ToList();

            _logger.LogWarning(
                "Password reset failed for user {UserId}: {Codes}",
                user.Id,
                string.Join(", ", result.Errors.Select(e => e.Code)));

            return new PasswordResetResult(false, errors);
        }

        private string BuildResetUrl(string email, string encodedToken)
        {
            var baseUrl = PublicBaseUrl.Resolve(_configuration);

            if (baseUrl is null)
            {
                // A relative link is useless in an email, so make the
                // misconfiguration visible instead of sending a broken message.
                throw new InvalidOperationException(
                    "WITCHERHUB_PUBLIC_BASE_URL is not configured, so a password reset link cannot be built.");
            }

            // The host in the link comes from configuration, never from the request,
            // so a forged Host header cannot redirect a reset link to another site.
            // The cost is that a wrong value silently sends users to the wrong
            // environment, which is why it is logged here and at start-up.
            _logger.LogInformation("Building a password reset link against {BaseUrl}.", baseUrl);

            return $"{baseUrl}/Auth/ResetPassword" +
                   $"?email={Uri.EscapeDataString(email)}" +
                   $"&token={Uri.EscapeDataString(encodedToken)}";
        }
    }
}
