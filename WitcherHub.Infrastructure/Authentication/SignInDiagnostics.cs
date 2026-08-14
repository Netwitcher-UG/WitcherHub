using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WitcherHub.Application.Interfaces;
using WitcherHub.Domain.SeedData;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;

namespace WitcherHub.Infrastructure.Authentication
{
    /// <summary>
    /// Answers "why did that sign-in fail" from the state of the running instance.
    ///
    /// Every fact here was chosen because its absence has already cost a debugging
    /// session: which database is behind this hostname, how many accounts it holds,
    /// whether the address exists at all, and whether a start-up password override
    /// is quietly rewriting the password on every deploy.
    /// </summary>
    public sealed class SignInDiagnostics : ISignInDiagnostics
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly AppDbContext _db;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<SignInDiagnostics> _logger;

        public SignInDiagnostics(
            UserManager<AppUser> userManager,
            AppDbContext db,
            IConfiguration configuration,
            IHostEnvironment environment,
            ILogger<SignInDiagnostics> logger)
        {
            _userManager = userManager;
            _db = db;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        /// <summary>
        /// The report names accounts that exist, so it stays behind a switch.
        /// Development has it on, because there the alternative is guessing.
        /// </summary>
        public bool IsEnabled =>
            _configuration.GetValue("Auth:ShowSignInDiagnostics", _environment.IsDevelopment());

        public async Task<SignInDiagnosticsReport> DescribeAsync(string email, CancellationToken ct = default)
        {
            var facts = new List<SignInDiagnosticFact>();

            try
            {
                facts.Add(new("Environment", _environment.EnvironmentName));

                // Which database, without the credentials that reach it. A
                // connection string must never be printed; host and catalogue are
                // what actually answers "am I looking at dev or production".
                try
                {
                    var connection = _db.Database.GetDbConnection();
                    facts.Add(new("Database host", string.IsNullOrWhiteSpace(connection.DataSource)
                        ? "(not reported by the provider)"
                        : connection.DataSource));
                    facts.Add(new("Database name", string.IsNullOrWhiteSpace(connection.Database)
                        ? "(not reported by the provider)"
                        : connection.Database));
                }
                catch (Exception ex)
                {
                    facts.Add(new("Database", $"could not be identified ({ex.GetType().Name})"));
                }

                var totalAccounts = await _userManager.Users.CountAsync(ct);
                facts.Add(new("Accounts in this database", totalAccounts.ToString()));

                var normalized = (email ?? "").Trim();
                var user = normalized.Length == 0 ? null : await _userManager.FindByEmailAsync(normalized);

                facts.Add(new("Account exists here", user is null ? "no" : "yes"));

                if (user is not null)
                {
                    facts.Add(new("Password stored", user.PasswordHash is null ? "no" : "yes"));
                    facts.Add(new("Email confirmed", user.EmailConfirmed ? "yes" : "no"));
                    facts.Add(new("Locked out", await _userManager.IsLockedOutAsync(user) ? "yes" : "no"));

                    var roles = await _userManager.GetRolesAsync(user);
                    facts.Add(new("Roles", roles.Count == 0 ? "(none)" : string.Join(", ", roles)));
                    facts.Add(new("Has the Admin role",
                        roles.Contains(AppRoles.Admin, StringComparer.OrdinalIgnoreCase) ? "yes" : "no"));
                }

                // The single most common cause of "the password I set no longer
                // works": a start-up override that rewrites it on every deploy.
                var bootstrapEmail = _configuration["BootstrapAdmin:Email"]?.Trim();
                var overrideOnStartup = _configuration.GetValue<bool>("BootstrapAdmin:ResetPasswordOnStartup");
                var overridePasswordConfigured = !string.IsNullOrWhiteSpace(_configuration["BootstrapAdmin:Password"]);

                if (overrideOnStartup && overridePasswordConfigured &&
                    string.Equals(bootstrapEmail, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add(new("Start-up password override",
                        "ACTIVE for this address. BootstrapAdmin__ResetPasswordOnStartup is true and " +
                        "BootstrapAdmin__Password is set, so this account's password is overwritten from " +
                        "configuration on every deploy. Only the value in BootstrapAdmin__Password will " +
                        "work; a password set through 'Forgot password?' is discarded at the next deploy. " +
                        "Remove both variables once you are signed in."));
                }
                else if (overrideOnStartup && !overridePasswordConfigured)
                {
                    facts.Add(new("Start-up password override",
                        "BootstrapAdmin__ResetPasswordOnStartup is true but BootstrapAdmin__Password is " +
                        "empty, so no password was applied."));
                }
                else
                {
                    facts.Add(new("Start-up password override", "not active"));
                }

                if (totalAccounts > 0)
                {
                    var known = await _userManager.Users
                        .OrderBy(u => u.Email)
                        .Select(u => u.Email)
                        .Take(25)
                        .ToListAsync(ct);

                    facts.Add(new("Addresses in this database", string.Join(", ", known)));
                }
            }
            catch (Exception ex)
            {
                // A diagnostic that throws replaces the problem it was meant to
                // explain, which is strictly worse than an incomplete report.
                _logger.LogWarning(ex, "Sign-in diagnostics could not be collected.");
                facts.Add(new("Diagnostics", $"incomplete — {ex.GetType().Name}: {ex.GetBaseException().Message}"));
            }

            return new SignInDiagnosticsReport(facts);
        }
    }
}
