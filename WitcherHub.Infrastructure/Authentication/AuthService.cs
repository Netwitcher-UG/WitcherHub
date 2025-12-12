using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using WitcherHub.Application.Interfaces;
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

        public AuthService(
            UserManager<AppUser> userManager,
            RoleManager<IdentityRole<Guid>> roleManager,
            IOptions<JwtOptions> jwtOptions,
            ILogger<AuthService> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _jwt = jwtOptions.Value;
            _logger = logger;
        }

        public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
                throw new InvalidOperationException("Invalid credentials.");

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
    }
}
