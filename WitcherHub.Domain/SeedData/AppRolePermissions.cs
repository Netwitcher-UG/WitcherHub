
namespace WitcherHub.Domain.SeedData
{
    public static class AppRolePermissions
    {
        public static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> Map
            = new Dictionary<string, IReadOnlyCollection<string>>
            {

                // لا نضيف للادمن هنا لانه يأخذ كل البيرمشن فقط باقي الرولات مستقبلا لتحديد البيرمشن الافتراضي لها 
                // مثال
                // { AppRoles.Admin, new[] { AppPermissions.ManageNetwitcher } }
            };
    }
}
