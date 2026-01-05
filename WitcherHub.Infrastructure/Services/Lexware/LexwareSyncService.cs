using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WitcherHub.Application.Common.CacheKeys;
using WitcherHub.Application.Common.Caching;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.View.Customers;
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

        private static Customer MapLexwareContactToCustomer(JsonElement root, string lexId)
        {
            var orgId = EmptyToNull(S(Get(root, "organizationId")));
            var version = GetInt(Get(root, "version"));
            var archived = GetBool(Get(root, "archived"));
            var allowTaxFree = GetBool(Get(root, "company", "allowTaxFreeInvoices"));
            var customerNumber = GetInt(Get(root, "roles", "customer", "number"));

            var taxId = EmptyToNull(S(Get(root, "company", "vatRegistrationId")));
            var notes = EmptyToNull(S(Get(root, "note")));

            var phone = EmptyToNull(GetPrimaryContactPersonPhone(root));

            var hasCompany = Get(root, "company")?.ValueKind == JsonValueKind.Object;
            var type = hasCompany ? CustomerType.Company : CustomerType.Individual;

            string? firstName = null;
            string? lastName = null;

            // ✅ Company name
            var name = S(Get(root, "company", "name")).Trim();

            // ✅ Individual name + First/Last
            if (type == CustomerType.Individual)
            {
                firstName = EmptyToNull(S(Get(root, "person", "firstName")).Trim());
                lastName = EmptyToNull(S(Get(root, "person", "lastName")).Trim());

                name = ((firstName ?? "") + " " + (lastName ?? "")).Trim();

                // ✅ fallback if Lexware did not return person
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = S(Get(root, "name")).Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(name))
                name = ""; // required in your DB

            var customer = new Customer
            {
                Type = type,
                Name = name,

                // ✅ NEW fields
                FirstName = firstName,
                LastName = lastName,

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

            // Emails
            var lexEmails = DistinctEmails(
                ReadEmailAddresses(root).Select(x => (x.Email ?? "", x.Kind ?? "business"))
            );

            foreach (var e in lexEmails)
            {
                customer.EmailAddresses.Add(new CustomerEmailAddress
                {
                    Kind = e.Kind,
                    Email = e.Email
                });
            }

            // Addresses
            foreach (var addr in ReadAddresses(root))
            {
                customer.Addresses.Add(new CustomerAddress
                {
                    FullNameOrCompany = customer.Name,
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

            // Contacts
            foreach (var p in ReadContactPersons(root))
            {
                customer.Contacts.Add(new CustomerContact
                {
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
        private static string NormEmail(string? e) => (e ?? "").Trim().ToLowerInvariant();
        private static string NormKind(string? k) => string.IsNullOrWhiteSpace(k) ? "business" : k.Trim().ToLowerInvariant();

        private static int KindRank(string kind) => kind switch
        {
            "business" => 3,
            "other" => 2,
            "private" => 1,
            _ => 0
        };

        private static List<(string Email, string Kind)> DistinctEmails(IEnumerable<(string Email, string Kind)> src)
        {
            return src
                .Select(x => (Email: NormEmail(x.Email), Kind: NormKind(x.Kind)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Email))
                .GroupBy(x => x.Email)
                .Select(g => g.OrderByDescending(x => KindRank(x.Kind)).First())
                .ToList();
        }
        public async Task<CustomerViews.CustomerDetailsView> ExportCustomerAsync(Guid customerId, CancellationToken ct = default)
        {
            var repo = _uow.Repo<Customer>();

            var customer = await repo.Query(asNoTracking: false)
            .Include(x => x.EmailAddresses)
            .Include(x => x.Addresses)
            .Include(x => x.Contacts)
            .FirstOrDefaultAsync(x => x.Id == customerId, ct);

            if (customer == null)
                throw new NotFoundAppException("Customer not found.");

            if (customer.LexwareType != LexwareType.NotExported)
                throw new BadRequestAppException("Customer cannot be exported.");

            var payload = BuildLexwareCreatePayload(customer);

            var created = await _lexware.CreateContactAsync(payload, ct);

            // اقرأ القيم من Lexware response
            var lexId = S(Get(created, "id"));
            var orgId = EmptyToNull(S(Get(created, "organizationId")));
            var version = GetInt(Get(created, "version"));
            var archived = GetBool(Get(created, "archived"));
            var allowTaxFree = GetBool(Get(created, "company", "allowTaxFreeInvoices"));
            var customerNumber = GetInt(Get(created, "roles", "customer", "number"));

            customer.LexwareType = LexwareType.Exported;
            customer.LexwareContactId = lexId;
            customer.LexwareOrganizationId = orgId;
            customer.LexwareVersion = version;
            customer.LexwareArchived = archived;
            customer.LexwareAllowTaxFreeInvoices = allowTaxFree;
            customer.LexwareCustomerNumber = customerNumber;
            customer.LexwareSyncedAtUtc = DateTime.UtcNow;

            // Mark child records as lexware (optional)
            foreach (var a in customer.Addresses) a.IsLexware = true;
            foreach (var c in customer.Contacts) c.IsLexware = true;

            await _uow.SaveChangesAsync(ct);

            // invalidate cache
            await _cache.RemoveAsync(CustomerCacheKeys.Details(customerId), ct);
            await _cache.BumpVersionAsync(CustomerCacheKeys.ListVersionKey, ct);

            // ✅ map entity -> CustomerDetailsView
            return MapCustomerToDetailsView(customer);

        }
        private static CustomerViews.CustomerDetailsView MapCustomerToDetailsView(Customer entity)
        {
            var primaryEmail = entity.EmailAddresses
                .OrderByDescending(e => e.Kind == "business")
                .ThenBy(e => e.Email)
                .Select(e => e.Email)
                .FirstOrDefault();

            return new CustomerViews.CustomerDetailsView
            {
                Id = entity.Id,
                Type = entity.Type,
                Name = entity.Name,
                Email = primaryEmail,
                Phone = entity.Phone,
                TaxId = entity.TaxId,
                Notes = entity.Notes,
                LexwareType = entity.LexwareType,

                LexwareCustomerNumber = entity.LexwareCustomerNumber,
                LexwareContactId = entity.LexwareContactId,
                LexwareOrganizationId = entity.LexwareOrganizationId,
                LexwareVersion = entity.LexwareVersion,
                LexwareArchived = entity.LexwareArchived,
                LexwareAllowTaxFreeInvoices = entity.LexwareAllowTaxFreeInvoices,
                LexwareSyncedAtUtc = entity.LexwareSyncedAtUtc,

                EmailAddresses = entity.EmailAddresses
                    .OrderByDescending(e => e.Kind == "business")
                    .ThenBy(e => e.Email)
                    .Select(e => new CustomerViews.CustomerEmailAddressItemView
                    {
                        Id = e.Id,
                        Kind = e.Kind,
                        Email = e.Email
                    }).ToList(),

                Addresses = entity.Addresses
                    .OrderByDescending(a => a.IsDefault)
                    .ThenBy(a => a.Label)
                    .Select(a => new CustomerViews.CustomerAddressItemView
                    {
                        Id = a.Id,
                        Label = a.Label,
                        Country = a.Country,
                        CountryCode = a.CountryCode,
                        City = a.City,
                        PostalCode = a.PostalCode,
                        FullNameOrCompany = a.FullNameOrCompany,
                        StreetRaw = a.StreetRaw,
                        AddressLine2 = a.AddressLine2,
                        IsDefault = a.IsDefault
                    }).ToList(),

                Contacts = entity.Contacts
                    .OrderByDescending(x => x.IsPrimary)
                    .ThenBy(x => x.Name)
                    .Select(x => new CustomerViews.CustomerContactItemView
                    {
                        Id = x.Id,
                        Name = x.Name,
                        Position = x.Position,
                        Email = x.Email,
                        Phone = x.Phone,
                        Salutation = x.Salutation,
                        FirstName = x.FirstName,
                        LastName = x.LastName,
                        IsPrimary = x.IsPrimary
                    }).ToList()
            };
        }

        public async Task<CustomerViews.CustomerDetailsView> DeleteCustomerFromLexwareAsync(string lexwareContactId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(lexwareContactId))
                throw new BadRequestAppException("Invalid Lexware contact id.");

            var repo = _uow.Repo<Customer>();

            var customer = await repo.Query(asNoTracking: false)
                .Include(x => x.EmailAddresses)
                .Include(x => x.Addresses)
                .Include(x => x.Contacts)
                .FirstOrDefaultAsync(x => x.LexwareContactId == lexwareContactId, ct);

            if (customer == null)
                throw new NotFoundAppException("Customer not found.");

            await _lexware.DeleteContactAsync(customer.LexwareContactId!, ct);

            customer.LexwareType = LexwareType.NotExported;
            customer.LexwareContactId = null;
            customer.LexwareOrganizationId = null;
            customer.LexwareCustomerNumber = null;
            customer.LexwareVersion = null;
            customer.LexwareArchived = null;
            customer.LexwareAllowTaxFreeInvoices = null;
            customer.LexwareSyncedAtUtc = null;

            await _uow.SaveChangesAsync(ct);

            await _cache.RemoveAsync(CustomerCacheKeys.Details(customer.Id), ct);
            await _cache.BumpVersionAsync(CustomerCacheKeys.ListVersionKey, ct);

            return MapCustomerToDetailsView(customer);
        }



        private static object BuildLexwareCreatePayload(Customer c)
        {
            var defaultAddr = c.Addresses.OrderByDescending(a => a.IsDefault).FirstOrDefault();
            var emails = c.EmailAddresses.Select(e => e.Email).Distinct().ToList();

            if (c.Type == CustomerType.Company)
            {
                var contactPersons = c.Contacts.Select(x => new
                {
                    salutation = x.Salutation,
                    firstName = x.FirstName,
                    lastName = x.LastName,
                    emailAddress = x.Email,
                    phoneNumber = x.Phone,
                    primary = x.IsPrimary
                }).ToList();

                return new
                {
                    company = new
                    {
                        name = c.Name,
                        vatRegistrationId = c.TaxId,
                        allowTaxFreeInvoices = false,
                        contactPersons = contactPersons
                    },
                    emailAddresses = new
                    {
                        business = emails
                    },
                    addresses = new
                    {
                        billing = defaultAddr == null ? new object[] { } : new[]
                        {
                    new {
                        street = defaultAddr.StreetRaw,
                        zip = defaultAddr.PostalCode,
                        city = defaultAddr.City,
                        countryCode = defaultAddr.CountryCode,
                        supplement = defaultAddr.AddressLine2
                    }
                }
                    },
                    roles = new
                    {
                        customer = new { number = (int?)null }
                    }
                };
            }
            else
            {
                var fn = !string.IsNullOrWhiteSpace(c.FirstName) ? c.FirstName : "";
                var ln = !string.IsNullOrWhiteSpace(c.LastName) ? c.LastName : "";

                // fallback to Name if first/last empty
                if (string.IsNullOrWhiteSpace(fn) && string.IsNullOrWhiteSpace(ln))
                {
                    var split = (c.Name ?? "").Trim().Split(' ', 2);
                    fn = split.Length > 0 ? split[0] : "";
                    ln = split.Length > 1 ? split[1] : "";
                }

                // ✅ Strong fallback (Lexware may reject empty person)
                if (string.IsNullOrWhiteSpace(fn) && string.IsNullOrWhiteSpace(ln))
                {
                    fn = "Unknown";
                    ln = "Customer";
                }

                return new
                {
                    person = new
                    {
                        firstName = fn,
                        lastName = ln
                    },
                    emailAddresses = new
                    {
                        business = emails
                    },
                    addresses = new
                    {
                        billing = defaultAddr == null ? new object[] { } : new[]
                        {
                new {
                    street = defaultAddr.StreetRaw,
                    zip = defaultAddr.PostalCode,
                    city = defaultAddr.City,
                    countryCode = defaultAddr.CountryCode,
                    supplement = defaultAddr.AddressLine2
                }
            }
                    },
                    roles = new
                    {
                        customer = new { number = (int?)null }
                    }
                };
            }

        }



    }
}
