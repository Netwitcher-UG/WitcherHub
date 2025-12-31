using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WitcherHub.Application.Common.CacheKeys;
using WitcherHub.Application.Common.Caching;
using WitcherHub.Application.Interfaces;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Services.Lexware
{
    public sealed class LexwareSyncService : ILexwareSyncService
    {
        private readonly ILexwareClient _lexware;
        private readonly IUnitOfWork _uow;
        private readonly IAppCache _cache;

        public LexwareSyncService(ILexwareClient lexware, IUnitOfWork uow, IAppCache cache)
        {
            _lexware = lexware;
            _uow = uow;
            _cache = cache;
        }

        public async Task<LexwareImportResult> ImportAllContactsAsync(CancellationToken ct = default)
        {
            var result = new LexwareImportResult();

            var items = await _lexware.GetAllContactsAsync(ct);
            result.TotalFromLexware = items.Count;

            // Existing Lexware IDs (we only CREATE new ones)
            var existingIds = await _uow.Repo<Customer>()
                .Query(asNoTracking: true)
                .Where(c => c.LexwareContactId != null && c.LexwareContactId != "")
                .Select(c => c.LexwareContactId!)
                .ToListAsync(ct);

            var existingSet = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);

            await _uow.BeginTransactionAsync(ct);
            try
            {
                foreach (var el in items)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var lexId = S(Get(el, "id"));
                        if (lexId.Length == 0)
                        {
                            result.Failed++;
                            result.Errors.Add("Lexware contact missing id.");
                            continue;
                        }

                        // شرطك: إذا تم جلبه قبل -> نتجاوزه ولا نحدّثه
                        if (existingSet.Contains(lexId))
                        {
                            result.Skipped++;
                            continue;
                        }

                        var customer = MapLexwareContactToCustomer(el, lexId);

                        await _uow.Repo<Customer>().AddAsync(customer, ct);

                        existingSet.Add(lexId);
                        result.Created++;
                    }
                    catch (Exception exOne)
                    {
                        result.Failed++;
                        result.Errors.Add(exOne.Message);
                    }
                }

                await _uow.CommitTransactionAsync(ct);

                // ✅ أهم جزء لحل مشكلة “ما تظهر فورًا”:
                // يكسر كاش اللست مباشرة لأن المفاتيح مبنية على version
                await _cache.BumpVersionAsync(CustomerCacheKeys.ListVersionKey, ct);

                return result;
            }
            catch
            {
                await _uow.RollbackTransactionAsync(ct);
                throw;
            }
        }

        // =========================
        // Mapping (Lexware JSON -> Your Models)
        // =========================
        private static Customer MapLexwareContactToCustomer(JsonElement root, string lexId)
        {
            var hasCompany = Get(root, "company")?.ValueKind == JsonValueKind.Object;
            var type = hasCompany ? CustomerType.Company : CustomerType.Individual;

            // Name (company.name) OR fallback person
            var name = S(Get(root, "company", "name")).Trim();
            if (name.Length == 0)
            {
                var fn = S(Get(root, "person", "firstName"));
                var ln = S(Get(root, "person", "lastName"));
                name = (fn + " " + ln).Trim();
            }
            if (name.Length == 0) name = ""; // required

            var orgId = EmptyToNull(S(Get(root, "organizationId")));
            var version = GetInt(Get(root, "version"));
            var archived = GetBool(Get(root, "archived"));
            var allowTaxFree = GetBool(Get(root, "company", "allowTaxFreeInvoices"));
            var customerNumber = GetInt(Get(root, "roles", "customer", "number"));

            var taxId = EmptyToNull(S(Get(root, "company", "vatRegistrationId")));
            var notes = EmptyToNull(S(Get(root, "note")));

            var phone = EmptyToNull(GetPrimaryContactPersonPhone(root));

            var customer = new Customer
            {
                // ❌ لا Id هنا (EF يولده عندك)
                Type = type,
                Name = name,
                Phone = phone,
                TaxId = taxId,
                Notes = notes,

                LexwareType = LexwareType.Imported,
                LexwareContactId = lexId,
                LexwareOrganizationId = orgId,
                LexwareCustomerNumber = customerNumber,
                LexwareVersion = version,
                LexwareArchived = archived,
                LexwareAllowTaxFreeInvoices = allowTaxFree,
                LexwareSyncedAtUtc = DateTime.UtcNow
            };

            // Emails: emailAddresses.business[] etc + fallback contactPersons.emailAddress
            foreach (var (kind, email) in ReadEmailAddresses(root))
            {
                // Required string -> "" لو null
                var safeEmail = email ?? "";
                var safeKind = string.IsNullOrWhiteSpace(kind) ? "business" : kind.Trim();

                // إذا كل شيء فاضي، لا نضيف سطر (اختياري)
                // لو بدك تضيف حتى الفاضي احذف الشرط
                if (safeEmail.Length == 0) continue;

                customer.EmailAddresses.Add(new CustomerEmailAddress
                {
                    // ❌ لا Id ولا CustomerId
                    Kind = safeKind,
                    Email = safeEmail
                });
            }

            // Addresses: addresses.billing[] etc
            foreach (var addr in ReadAddresses(root))
            {
                customer.Addresses.Add(new CustomerAddress
                {
                    // ❌ لا Id ولا CustomerId
                    FullNameOrCompany = customer.Name, // required
                    Label = EmptyToNull(addr.Label),
                    StreetRaw = EmptyToNull(addr.StreetRaw),
                    AddressLine2 = EmptyToNull(addr.AddressLine2),
                    City = EmptyToNull(addr.City),
                    PostalCode = EmptyToNull(addr.PostalCode),
                    CountryCode = EmptyToNull(addr.CountryCode),
                    Country = null,
                    IsDefault = addr.IsDefault,
                    IsLexware = true
                });
            }

            // Contacts: company.contactPersons[]
            foreach (var p in ReadContactPersons(root))
            {
                customer.Contacts.Add(new CustomerContact
                {
                    // ❌ لا Id ولا CustomerId
                    Name = p.Name ?? "",
                    Email = EmptyToNull(p.Email),
                    Phone = EmptyToNull(p.Phone),
                    Position = null,
                    Salutation = EmptyToNull(p.Salutation),
                    FirstName = EmptyToNull(p.FirstName),
                    LastName = EmptyToNull(p.LastName),
                    IsPrimary = p.IsPrimary,
                    IsLexware = true
                });
            }

            return customer;
        }

        // =========================
        // Lexware readers
        // =========================
        private static string? GetPrimaryContactPersonPhone(JsonElement root)
        {
            var cps = Get(root, "company", "contactPersons");
            if (cps is null || cps.Value.ValueKind != JsonValueKind.Array) return null;

            foreach (var p in cps.Value.EnumerateArray())
            {
                var isPrimary = GetBool(Get(p, "primary")) ?? false;
                if (isPrimary)
                    return EmptyToNull(S(Get(p, "phoneNumber")));
            }

            var first = cps.Value.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Undefined)
                return EmptyToNull(S(Get(first, "phoneNumber")));

            return null;
        }

        private static IEnumerable<(string Kind, string Email)> ReadEmailAddresses(JsonElement root)
        {
            // shape: emailAddresses: { business:[...], private:[...], other:[...] }
            var emailsObj = Get(root, "emailAddresses");
            if (emailsObj is not null && emailsObj.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in emailsObj.Value.EnumerateObject())
                {
                    var kind = prop.Name;
                    if (prop.Value.ValueKind != JsonValueKind.Array) continue;

                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        var email = item.ValueKind == JsonValueKind.String
                            ? (item.GetString() ?? "")
                            : (item.ToString() ?? "");

                        if (!string.IsNullOrWhiteSpace(email))
                            yield return (kind, email.Trim());
                    }
                }
            }

            // fallback: contactPersons.emailAddress
            var cps = Get(root, "company", "contactPersons");
            if (cps is not null && cps.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in cps.Value.EnumerateArray())
                {
                    var em = S(Get(p, "emailAddress")).Trim();
                    if (em.Length > 0)
                        yield return ("business", em);
                }
            }
        }

        private sealed record LexAddr(
            string Label,
            string StreetRaw,
            string AddressLine2,
            string City,
            string PostalCode,
            string CountryCode,
            bool IsDefault
        );

        private static IEnumerable<LexAddr> ReadAddresses(JsonElement root)
        {
            // shape: addresses: { billing:[{street,zip,city,countryCode,supplement?...}], shipping:[...] }
            var addrObj = Get(root, "addresses");
            if (addrObj is null || addrObj.Value.ValueKind != JsonValueKind.Object)
                yield break;

            var defaultSet = false;

            foreach (var group in addrObj.Value.EnumerateObject())
            {
                var label = group.Name; // billing / shipping / ...
                if (group.Value.ValueKind != JsonValueKind.Array) continue;

                foreach (var a in group.Value.EnumerateArray())
                {
                    var street = S(Get(a, "street"));
                    var zip = S(Get(a, "zip"));
                    var city = S(Get(a, "city"));
                    var code = S(Get(a, "countryCode"));
                    var supplement = S(Get(a, "supplement")); // Lexware uses supplement

                    var isDefault = false;
                    if (!defaultSet && label.Equals("billing", StringComparison.OrdinalIgnoreCase))
                    {
                        isDefault = true;
                        defaultSet = true;
                    }

                    yield return new LexAddr(
                        Label: label,
                        StreetRaw: street,
                        AddressLine2: supplement,
                        City: city,
                        PostalCode: zip,
                        CountryCode: code,
                        IsDefault: isDefault
                    );
                }
            }
        }

        private sealed record LexPerson(
            string Name,
            string? Email,
            string? Phone,
            string? Salutation,
            string? FirstName,
            string? LastName,
            bool IsPrimary
        );

        private static IEnumerable<LexPerson> ReadContactPersons(JsonElement root)
        {
            var cps = Get(root, "company", "contactPersons");
            if (cps is null || cps.Value.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var p in cps.Value.EnumerateArray())
            {
                var sal = EmptyToNull(S(Get(p, "salutation")));
                var fn = EmptyToNull(S(Get(p, "firstName")));
                var ln = EmptyToNull(S(Get(p, "lastName")));
                var full = ((fn ?? "") + " " + (ln ?? "")).Trim();
                var name = full.Length > 0 ? full : "";

                var isPrimary = GetBool(Get(p, "primary")) ?? false;

                yield return new LexPerson(
                    Name: name,
                    Email: EmptyToNull(S(Get(p, "emailAddress"))),
                    Phone: EmptyToNull(S(Get(p, "phoneNumber"))),
                    Salutation: sal,
                    FirstName: fn,
                    LastName: ln,
                    IsPrimary: isPrimary
                );
            }
        }

        // =========================
        // Json helpers
        // =========================
        private static string S(JsonElement? el)
        {
            if (el is null) return "";
            var v = el.Value;
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            if (v.ValueKind == JsonValueKind.Null) return "";
            return v.ToString() ?? "";
        }

        private static int? GetInt(JsonElement? el)
        {
            if (el is null) return null;
            var v = el.Value;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var n2)) return n2;
            return null;
        }

        private static bool? GetBool(JsonElement? el)
        {
            if (el is null) return null;
            var v = el.Value;
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
            return null;
        }

        private static string? EmptyToNull(string s)
            => string.IsNullOrWhiteSpace(s) ? null : s;

        private static JsonElement? Get(JsonElement root, params string[] path)
        {
            JsonElement cur = root;

            foreach (var key in path)
            {
                if (cur.ValueKind == JsonValueKind.Object)
                {
                    if (cur.TryGetProperty(key, out var next))
                    {
                        cur = next;
                        continue;
                    }

                    if (key.Length > 1)
                    {
                        var camel = char.ToLowerInvariant(key[0]) + key[1..];
                        if (cur.TryGetProperty(camel, out next))
                        {
                            cur = next;
                            continue;
                        }

                        var pascal = char.ToUpperInvariant(key[0]) + key[1..];
                        if (cur.TryGetProperty(pascal, out next))
                        {
                            cur = next;
                            continue;
                        }
                    }
                }

                return null;
            }

            return cur;
        }
    }
}
