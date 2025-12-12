using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
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

        public IdentityDataSeeder(
            RoleManager<IdentityRole<Guid>> roleManager,
            UserManager<AppUser> userManager,
            ILogger<IdentityDataSeeder> logger)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task SeedAsync(CancellationToken ct = default)
        {
            await EnsureRolesAsync();
            await EnsureRolePermissionsAsync();
            await EnsureDefaultAdminUserIfUsersEmptyAsync(ct);
        }

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

            const string email = "basel.slaby@gmail.com";
            const string password = "Ww@12345";

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
