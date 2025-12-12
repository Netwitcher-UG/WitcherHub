using Microsoft.AspNetCore.Authorization;

namespace WitcherHub.Configuration.Authorization
{
    public sealed class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string permission) => Permission = permission;
        public string Permission { get; }
    }
}
