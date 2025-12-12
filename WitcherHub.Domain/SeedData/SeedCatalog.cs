
using System.Reflection;

namespace WitcherHub.Domain.SeedData
{
    public static class SeedCatalog
    {
        public static IReadOnlyList<string> Roles => GetPublicConstStrings(typeof(AppRoles));
        public static IReadOnlyList<string> Permissions => GetPublicConstStrings(typeof(AppPermissions));

        private static IReadOnlyList<string> GetPublicConstStrings(Type type)
            => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                   .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                   .Select(f => (string)f.GetRawConstantValue()!)
                   .Distinct()
                   .ToArray();
    }
}
