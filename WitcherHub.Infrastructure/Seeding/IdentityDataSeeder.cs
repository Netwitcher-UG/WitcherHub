using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Security.Cryptography;
using WitcherHub.Application.Interfaces;
using WitcherHub.Domain.SeedData;
using WitcherHub.Infrastructure.Data.Models;

namespace WitcherHub.Infrastructure.Seeding
{
    public sealed class IdentityDataSeeder : IDataSeeder
    {
        private readonly RoleManager<IdentityRole<Guid>> _roleManager;
        private readonly UserManager<AppUser> _userManager;
        private readonly ILogger<IdentityDataSeeder> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;

        public IdentityDataSeeder(
            RoleManager<IdentityRole<Guid>> roleManager,
            UserManager<AppUser> userManager,
            ILogger<IdentityDataSeeder> logger,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _logger = logger;
            _configuration = configuration;
            _environment = environment;
        }

        public async Task SeedAsync(CancellationToken ct = default)
        {
            await EnsureRolesAsync();
            await EnsureRolePermissionsAsync();
            await EnsureDefaultAdminUserIfUsersEmptyAsync(ct);
            await EnsureBootstrapAdminAsync(ct);
            await LogSignInInventoryAsync(ct);
        }

        /// <summary>
        /// Lists the accounts that can sign in to the database this instance is
        /// actually connected to.
        ///
        /// With two environments pointing at two databases, "the login says my
        /// details are wrong" is usually "that account is in the other database",
        /// and there was no way to tell from the outside.
        /// </summary>
        private async Task LogSignInInventoryAsync(CancellationToken ct)
        {
            try
            {
                var accounts = await _userManager.Users
                    .Select(u => new { u.Email, u.EmailConfirmed })
                    .OrderBy(u => u.Email)
                    .ToListAsync(ct);

                if (accounts.Count == 0)
                {
                    _logger.LogWarning("This database contains no user accounts, so nobody can sign in.");
                    return;
                }

                var adminEmails = (await _userManager.GetUsersInRoleAsync(AppRoles.Admin))
                    .Select(u => u.Email)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var summary = accounts.Select(a =>
                    $"{a.Email}{(adminEmails.Contains(a.Email ?? "") ? " [Admin]" : "")}" +
                    $"{(a.EmailConfirmed ? "" : " [email unconfirmed]")}");

                _logger.LogInformation(
                    "Accounts that can sign in to this database ({Count}): {Accounts}",
                    accounts.Count, string.Join(", ", summary));
            }
            catch (Exception ex)
            {
                // Diagnostics must never be the reason start-up fails.
                _logger.LogWarning(ex, "Could not list user accounts.");
            }
        }

        /// <summary>
        /// Guarantees that a nominated address holds the Admin role, which carries
        /// every permission in the system.
        ///
        /// Unlike <see cref="EnsureDefaultAdminUserIfUsersEmptyAsync"/> this runs on
        /// every start-up and works against a populated database, so an
        /// administrator can be provisioned on an environment that already has
        /// users. It is idempotent: an existing account keeps its password and only
        /// gains the role if it is missing.
        /// </summary>
        private async Task EnsureBootstrapAdminAsync(CancellationToken ct)
        {
            var email = _configuration["BootstrapAdmin:Email"]?.Trim();

            if (string.IsNullOrWhiteSpace(email))
                return;

            var existing = await _userManager.FindByEmailAsync(email);

            if (existing is not null)
            {
                await ApplyBreakGlassPasswordAsync(existing, email);

                if (await _userManager.IsInRoleAsync(existing, AppRoles.Admin))
                {
                    _logger.LogInformation(
                        "Bootstrap administrator {Email} already exists with the {Role} role.",
                        email, AppRoles.Admin);
                    return;
                }

                var promote = await _userManager.AddToRoleAsync(existing, AppRoles.Admin);

                if (promote.Succeeded)
                {
                    _logger.LogWarning(
                        "Existing user {Email} was granted the {Role} role via BootstrapAdmin configuration.",
                        email, AppRoles.Admin);
                }
                else
                {
                    _logger.LogError(
                        "Failed to grant {Role} to {Email}. Errors: {Errors}",
                        AppRoles.Admin, email,
                        string.Join(" | ", promote.Errors.Select(e => e.Description)));
                }

                return;
            }

            // A password may be supplied for the first sign-in. When it is not, the
            // account is created with a random one that is never recorded anywhere,
            // and access is obtained through the password reset flow instead — that
            // way no administrator credential has to be typed into configuration.
            var configuredPassword = _configuration["BootstrapAdmin:Password"];
            var usingRandomPassword = string.IsNullOrWhiteSpace(configuredPassword);
            var password = usingRandomPassword ? GenerateUnknowablePassword() : configuredPassword!;

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var create = await _userManager.CreateAsync(user, password);
            if (!create.Succeeded)
            {
                _logger.LogError(
                    "Failed to create bootstrap administrator {Email}. Errors: {Errors}",
                    email, string.Join(" | ", create.Errors.Select(e => e.Description)));
                return;
            }

            var addToRole = await _userManager.AddToRoleAsync(user, AppRoles.Admin);
            if (!addToRole.Succeeded)
            {
                _logger.LogError(
                    "Created {Email} but failed to assign the {Role} role. Errors: {Errors}",
                    email, AppRoles.Admin,
                    string.Join(" | ", addToRole.Errors.Select(e => e.Description)));
                return;
            }

            if (usingRandomPassword)
            {
                _logger.LogWarning(
                    "Bootstrap administrator {Email} created with the {Role} role and an unknown random " +
                    "password. Use 'Forgot password?' on the login page to set one. Requires a working " +
                    "SMTP configuration.",
                    email, AppRoles.Admin);
            }
            else
            {
                _logger.LogInformation(
                    "Bootstrap administrator {Email} created with the {Role} role using the configured password.",
                    email, AppRoles.Admin);
            }
        }

        /// <summary>
        /// Break-glass recovery: sets the password of an account that already exists.
        ///
        /// Needed because the bootstrap step deliberately never touches an existing
        /// password, which leaves no way back in when the reset email cannot be
        /// delivered — the situation on an environment with no SMTP configured.
        ///
        /// Requires both BootstrapAdmin__Password and an explicit
        /// BootstrapAdmin__ResetPasswordOnStartup=true, so it cannot happen by
        /// accident, and it announces itself in the log every time it runs.
        /// </summary>
        private async Task ApplyBreakGlassPasswordAsync(AppUser user, string email)
        {
            if (!_configuration.GetValue<bool>("BootstrapAdmin:ResetPasswordOnStartup"))
                return;

            var password = _configuration["BootstrapAdmin:Password"];

            if (string.IsNullOrWhiteSpace(password))
            {
                _logger.LogError(
                    "BootstrapAdmin__ResetPasswordOnStartup is set but BootstrapAdmin__Password is empty, " +
                    "so the password for {Email} was left unchanged.", email);
                return;
            }

            try
            {
                // Check the new password BEFORE removing the old one.
                //
                // This used to remove first and add second. When the configured
                // password failed the policy — most easily by having no lowercase
                // letter, since RequireLowercase is the one rule left at its
                // default — the remove succeeded, the add failed, and the account
                // was left with no password at all. Strictly worse than before:
                // the break-glass locked the door it was there to open, and every
                // subsequent deploy did it again.
                var problems = await ValidatePasswordAsync(user, password!);

                if (problems.Count > 0)
                {
                    _logger.LogError(
                        "BootstrapAdmin__Password does not satisfy the password policy, so the password " +
                        "for {Email} was left exactly as it was. Reasons: {Reasons}. The policy requires: " +
                        "{Policy}.",
                        email,
                        string.Join(" | ", problems),
                        DescribePasswordPolicy());

                    return;
                }

                // Set the hash directly rather than going through a reset token.
                //
                // A token would be encrypted by the Data Protection stack, which
                // reads its key ring from the database — so a break-glass intended
                // to restore access could instead crash start-up before the schema
                // was ready. This path depends on nothing but the user store.
                var remove = await _userManager.RemovePasswordAsync(user);
                if (!remove.Succeeded && !remove.Errors.Any(e => e.Code == "PasswordMismatch"))
                {
                    // A user with no password yet is fine; anything else is not.
                    _logger.LogWarning(
                        "Could not clear the existing password for {Email}: {Errors}",
                        email, string.Join(" | ", remove.Errors.Select(e => e.Description)));
                }

                var result = await _userManager.AddPasswordAsync(user, password!);

                if (result.Succeeded)
                {
                    _logger.LogWarning(
                        "The password for {Email} was overwritten from configuration on start-up. " +
                        "Remove BootstrapAdmin__ResetPasswordOnStartup and BootstrapAdmin__Password now, " +
                        "otherwise the password is reset on every deploy.", email);
                }
                else
                {
                    // Validation passed, so reaching here means the store itself
                    // refused. The account now has no password; say so plainly
                    // rather than leaving it to be discovered at the login screen.
                    _logger.LogCritical(
                        "Failed to set the password for {Email} after clearing it, so the account now has " +
                        "NO password and cannot be signed in to. Errors: {Errors}",
                        email, string.Join(" | ", result.Errors.Select(e => $"{e.Code}: {e.Description}")));
                }
            }
            catch (Exception ex)
            {
                // Losing the break-glass is inconvenient. Refusing to start because
                // of it is worse, so this never propagates.
                _logger.LogError(
                    ex,
                    "The break-glass password could not be applied for {Email}. " +
                    "Start-up continues; the existing password is unchanged.", email);
            }
        }

        /// <summary>
        /// Runs the configured password validators without touching the account.
        /// Returns the reasons it was rejected, empty when it is acceptable.
        /// </summary>
        private async Task<IReadOnlyList<string>> ValidatePasswordAsync(AppUser user, string password)
        {
            var problems = new List<string>();

            foreach (var validator in _userManager.PasswordValidators)
            {
                var result = await validator.ValidateAsync(_userManager, user, password);

                if (!result.Succeeded)
                    problems.AddRange(result.Errors.Select(e => $"{e.Code}: {e.Description}"));
            }

            return problems;
        }

        /// <summary>
        /// The password policy in words, read from the options rather than
        /// restated, so the log cannot drift away from what is enforced.
        /// </summary>
        private string DescribePasswordPolicy()
        {
            var options = _userManager.Options.Password;
            var rules = new List<string> { $"at least {options.RequiredLength} characters" };

            if (options.RequireLowercase) rules.Add("a lowercase letter");
            if (options.RequireUppercase) rules.Add("an uppercase letter");
            if (options.RequireDigit) rules.Add("a digit");
            if (options.RequireNonAlphanumeric) rules.Add("a non-alphanumeric character");
            if (options.RequiredUniqueChars > 1) rules.Add($"{options.RequiredUniqueChars} distinct characters");

            return string.Join(", ", rules);
        }

        /// <summary>
        /// A password nobody knows, so the account can only be reached through a
        /// password reset. The leading characters keep it valid against a stricter
        /// password policy than the current one.
        /// </summary>
        private static string GenerateUnknowablePassword() =>
            "Aa1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        private async Task EnsureRolesAsync()
        {
            foreach (var roleName in SeedCatalog.Roles)
            {
                if (await _roleManager.RoleExistsAsync(roleName))
                    continue;

                var result = await _roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                if (!result.Succeeded)
                {
                    _logger.LogError("Failed to create role {Role}. Errors: {Errors}",
                        roleName, string.Join(" | ", result.Errors.Select(e => e.Description)));
                    continue;
                }

                _logger.LogInformation("Role created: {Role}", roleName);
            }
        }

        private async Task EnsureRolePermissionsAsync()
        {
            foreach (var roleName in SeedCatalog.Roles)
            {
                var role = await _roleManager.FindByNameAsync(roleName);
                if (role is null)
                    continue;

                // Admin يأخذ كل الصلاحيات تلقائياً
                IReadOnlyCollection<string> desiredPermissions =
                    roleName == AppRoles.Admin
                        ? SeedCatalog.Permissions
                        : (AppRolePermissions.Map.TryGetValue(roleName, out var perms)
                            ? perms
                            : Array.Empty<string>());

                if (desiredPermissions.Count == 0)
                    continue;

                var existingClaims = await _roleManager.GetClaimsAsync(role);

                foreach (var perm in desiredPermissions)
                {
                    var exists = existingClaims.Any(c =>
                        c.Type == AppClaimTypes.Permission &&
                        c.Value == perm);

                    if (exists)
                        continue;

                    var addClaim = await _roleManager.AddClaimAsync(role, new Claim(AppClaimTypes.Permission, perm));
                    if (!addClaim.Succeeded)
                    {
                        _logger.LogError("Failed to add permission {Permission} to role {Role}. Errors: {Errors}",
                            perm, roleName, string.Join(" | ", addClaim.Errors.Select(e => e.Description)));
                        continue;
                    }

                    _logger.LogInformation("Permission {Permission} added to role {Role}.", perm, roleName);
                }
            }
        }

        private async Task EnsureDefaultAdminUserIfUsersEmptyAsync(CancellationToken ct)
        {
            if (await _userManager.Users.AnyAsync(ct))
            {
                _logger.LogInformation("Users table is not empty. Skipping default admin user seeding.");
                return;
            }

            // Credentials come from configuration (SeedAdmin__Email /
            // SeedAdmin__Password). They used to be hard-coded here, which meant
            // the first account of every deployment shipped with a password that
            // is readable in the repository.
            var email = _configuration["SeedAdmin:Email"];
            var password = _configuration["SeedAdmin:Password"];

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                if (!_environment.IsDevelopment())
                {
                    // BootstrapAdmin runs immediately after this and creates an
                    // administrator of its own, so this is only a real problem when
                    // neither is configured. Reporting an error for a situation the
                    // next step resolves trains people to ignore the log.
                    var bootstrapConfigured = !string.IsNullOrWhiteSpace(_configuration["BootstrapAdmin:Email"]);

                    if (bootstrapConfigured)
                    {
                        _logger.LogInformation(
                            "No SeedAdmin is configured; the bootstrap administrator will be used instead.");
                    }
                    else
                    {
                        _logger.LogError(
                            "No users exist and no administrator is configured. Set SeedAdmin__Email and " +
                            "SeedAdmin__Password, or BootstrapAdmin__Email, then restart — otherwise nobody can sign in.");
                    }

                    return;
                }

                email = "admin@witcherhub.local";
                password = "Dev@12345";

                _logger.LogWarning(
                    "Seeding a development administrator with a well-known password ({Email}). " +
                    "Set SeedAdmin__Email and SeedAdmin__Password to override.", email);
            }

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var createUser = await _userManager.CreateAsync(user, password);
            if (!createUser.Succeeded)
            {
                _logger.LogError("Failed to create default admin user. Errors: {Errors}",
                    string.Join(" | ", createUser.Errors.Select(e => e.Description)));
                return;
            }

            var addToRole = await _userManager.AddToRoleAsync(user, AppRoles.Admin);
            if (!addToRole.Succeeded)
            {
                _logger.LogError("Failed to add default user to role {Role}. Errors: {Errors}",
                    AppRoles.Admin, string.Join(" | ", addToRole.Errors.Select(e => e.Description)));
                return;
            }

            _logger.LogInformation("Default admin user created and assigned to role {Role}. Email: {Email}",
                AppRoles.Admin, email);
        }
    }
}
