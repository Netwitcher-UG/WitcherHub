using Microsoft.AspNetCore.Authorization;
using WitcherHub.Domain.SeedData;

namespace WitcherHub.Configuration.Authorization
{
    public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            var has = context.User.Claims.Any(c =>
                c.Type == AppClaimTypes.Permission &&
                c.Value == requirement.Permission);

            if (has) context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
