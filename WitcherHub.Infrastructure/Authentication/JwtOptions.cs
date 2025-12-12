
namespace WitcherHub.Infrastructure.Authentication
{
    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";
        public string Issuer { get; init; } = default!;
        public string Audience { get; init; } = default!;
        public string Key { get; init; } = default!;
        public int AccessTokenMinutes { get; init; } = 60;
    }
}
