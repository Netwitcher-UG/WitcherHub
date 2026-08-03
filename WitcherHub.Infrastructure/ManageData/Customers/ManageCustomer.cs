using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WitcherHub.Application.Common.CacheKeys;
using WitcherHub.Application.Common.Caching;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Customers;
using WitcherHub.Application.Models.View.Customers;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;
using AddressEntity = WitcherHub.Infrastructure.Data.Models.CustomerAddress;
using ContactEntity = WitcherHub.Infrastructure.Data.Models.CustomerContact;
using CustomerEntity = WitcherHub.Infrastructure.Data.Models.Customer;
using EmailEntity = WitcherHub.Infrastructure.Data.Models.CustomerEmailAddress;

namespace WitcherHub.Infrastructure.ManageData.Customers
{
    public sealed class ManageCustomer : ICustomer
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppCache _cache;
        private readonly ILogger<ManageCustomer> _log;

        // Kept as a stable prefix so the Razor Page can turn this service error
        // into a field-level validation message.
        private const string DuplicateEmailMessagePrefix =
            "Email address is already used by another client: ";

        private static readonly AppCacheEntryOptions ListCacheOptions = new()
        {
            SlidingExpiration = TimeSpan.FromSeconds(30),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3)
        };

        private static readonly AppCacheEntryOptions DetailsCacheOptions = new()
        {
            SlidingExpiration = TimeSpan.FromMinutes(2),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        public ManageCustomer(IUnitOfWork unitOfWork, IAppCache cache, ILogger<ManageCustomer> log)
        {
            _unitOfWork = unitOfWork;
            _cache = cache;
            _log = log;
        }

        // =========================
        // Listing (Pagination + Search)
        // =========================
        public async Task<PagedResult<CustomerViews.CustomerListItemView>> GetCustomersAsync(
    int page = 1,
    int pageSize = 10,
    string? search = null,
    CancellationToken ct = default)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 200 ? 10 : pageSize;

            var version = await _cache.GetOrCreateVersionAsync(CustomerCacheKeys.ListVersionKey, ct);

            var normalizedSearch = string.IsNullOrWhiteSpace(search)
    ? null
    : search.Trim().ToLowerInvariant();

            var cacheKey = CustomerCacheKeys.ListWithVersion(page, pageSize, normalizedSearch, version);

            return await _cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    var repo = _unitOfWork.Repo<CustomerEntity>();

                    // DB-side query (fast)
                    var q = repo.Query(asNoTracking: true);

                    //if (!string.IsNullOrWhiteSpace(search))
                    //{
                    //    var s = search.Trim();
                    //    var escaped = EscapeLike(s);
                    //    var pattern = $"%{escaped}%";

                    //    // SQL Server safe LIKE with escape char '!'
                    //    q = q.Where(c =>
                    //            EF.Functions.Like(c.Name, pattern, "!") ||
                    //            (c.Phone != null && EF.Functions.Like(c.Phone, pattern, "!")) ||
                    //            (c.TaxId != null && EF.Functions.Like(c.TaxId, pattern, "!")) ||
                    //            c.EmailAddresses.Any(e => EF.Functions.Like(e.Email, pattern, "!"))
                    //        );
                    //}
                    if (!string.IsNullOrWhiteSpace(normalizedSearch))
                    {
                        var escaped = EscapeLike(normalizedSearch);
                        var pattern = $"%{escaped}%";

                        q = q.Where(c =>
                            EF.Functions.Like(c.Name.ToLower(), pattern, "!") ||

                            (c.Phone != null &&
                             EF.Functions.Like(c.Phone.ToLower(), pattern, "!")) ||

                            (c.TaxId != null &&
                             EF.Functions.Like(c.TaxId.ToLower(), pattern, "!")) ||

                            c.EmailAddresses.Any(e =>
                                EF.Functions.Like(e.Email.ToLower(), pattern, "!")) ||

                            c.Addresses.Any(a =>
                                a.City != null &&
                                EF.Functions.Like(a.City.ToLower(), pattern, "!"))
                        );
                    }
                    var total = await q.LongCountAsync(token);
                    if (total == 0)
                        return PagedResult<CustomerViews.CustomerListItemView>.Empty(page, pageSize);

                    var items = await q
                        .OrderByDescending(c => c.CreatedAt)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .Select(c => new CustomerViews.CustomerListItemView
                        {
                            Id = c.Id,
                            Type = c.Type,
                            Name = c.Name,
                            Email = c.EmailAddresses
                                    .OrderByDescending(e => e.Kind == "business")
                                    .ThenBy(e => e.Email)
                                    .Select(e => e.Email)
                                    .FirstOrDefault(),
                            Phone = c.Phone,
                            TaxId = c.TaxId,
                            LexwareType = c.LexwareType,

                            // Default city (DB-side)
                            City = c.Addresses
                                .OrderByDescending(a => a.IsDefault)
                                .Select(a => a.City)
                                .FirstOrDefault()
                        })
                        .ToListAsync(token);

                    return new PagedResult<CustomerViews.CustomerListItemView>
                    {
                        Items = items,
                        Page = page,
                        PageSize = pageSize,
                        TotalItems = total
                    };
                },
                ListCacheOptions,
                ct);

            static string EscapeLike(string input)
                => input
                    .Replace("!", "!!")
                    .Replace("%", "!%")
                    .Replace("_", "!_")
                    .Replace("[", "![");
        }
        // =========================
        // Details
        // =========================
        public async Task<CustomerViews.CustomerDetailsView?> GetCustomerAsync(Guid id, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");

            var cacheKey = CustomerCacheKeys.Details(id);

            return await _cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    var repo = _unitOfWork.Repo<CustomerEntity>();

                    var entity = await repo.GetByIdAsync(
                                id,
                                ct: token,
                                asNoTracking: true,
                                x => x.Addresses,
                                x => x.Contacts,
                                x => x.EmailAddresses);

                    if (entity is null) return null;

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
                        FirstName = entity.FirstName,
                        LastName = entity.LastName,
                        LexwareType = entity.LexwareType,

                        // ✅ Lexware read-only fields
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
                            })
                            .ToList(),

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
                            })
                            .ToList(),

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
                            })
                            .ToList()
                    };

                },
                DetailsCacheOptions,
                ct);
        }

        // =========================
        // Customer CRUD
        // =========================
        public async Task<Guid> CreateAsync(CustomerDTOs dto, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");

            var customerRepo = _unitOfWork.Repo<CustomerEntity>();


            var customer = new CustomerEntity
            {
                Type = dto.Customer.Type,
                FirstName = dto.Customer.FirstName,
                LastName = dto.Customer.LastName,
                Name = dto.Customer.Name ?? string.Empty,
                Phone = string.IsNullOrWhiteSpace(dto.Customer.Phone) ? null : dto.Customer.Phone.Trim(),
                TaxId = string.IsNullOrWhiteSpace(dto.Customer.TaxId) ? null : dto.Customer.TaxId.Trim(),
                Notes = string.IsNullOrWhiteSpace(dto.Customer.Notes) ? null : dto.Customer.Notes.Trim(),
                LexwareType = LexwareType.NotExported
            };

            NormalizeAndValidateCustomer(customer);

            // ✅ EmailAddresses required
            if (dto.Customer.EmailAddresses is null || dto.Customer.EmailAddresses.Count == 0)
                throw new BadRequestAppException("At least one email address is required.");

            var normalizedEmailEntries = dto.Customer.EmailAddresses
                .Select(e => (
                    Email: NormEmail(e.Email),
                    Kind: NormKind(e.Kind)))
                .Where(e => !string.IsNullOrWhiteSpace(e.Email))
                .ToList();

            if (normalizedEmailEntries.Count == 0)
                throw new BadRequestAppException("At least one valid email address is required.");

            // Reject duplicates inside the same form instead of silently removing them.
            var duplicateInRequest = normalizedEmailEntries
                .GroupBy(e => e.Email, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicateInRequest is not null)
                throw new BadRequestAppException(
                    $"Duplicate email in request: {duplicateInRequest.Key}");

            // Global uniqueness: the email must not belong to any existing customer.
            await EnsureEmailsAreUniqueAsync(
                normalizedEmailEntries.Select(e => e.Email),
                exceptCustomerId: null,
                ct);

            var emails = normalizedEmailEntries;

            foreach (var e in emails)
            {
                customer.EmailAddresses.Add(new EmailEntity
                {
                    Kind = e.Kind,
                    Email = e.Email
                });
            }


            // Address is mandatory for both Company and Individual customers.
            var address = dto.Address.Adapt<AddressEntity>();
            NormalizeAndValidateAddress(address, customer.Name);

            address.IsDefault = true;
            address.IsLexware = false;
            customer.Addresses.Add(address);

            // Company contact is mandatory. The create modal posts a full contact name;
            // the contact editor may post separate first/last names.
            if (dto.Customer.Type == CustomerType.Company)
            {
                if (dto.Contact is null)
                    throw new BadRequestAppException("Company contact is required.");

                var contact = dto.Contact.Adapt<ContactEntity>();
                NormalizeAndValidateCompanyContact(contact);
                contact.IsPrimary = true;
                contact.IsLexware = false;
                customer.Contacts.Add(contact);
            }

            await customerRepo.AddAsync(customer, ct);

            try
            {
                await _unitOfWork.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueEmailViolation(ex))
            {
                // Covers the race condition where two requests pass the pre-check
                // at the same time. The unique database index remains authoritative.
                var email = GetEmailFromUpdateException(ex)
                            ?? emails.FirstOrDefault().Email
                            ?? string.Empty;

                _log.LogWarning(ex, "Duplicate customer email rejected: {Email}", email);
                throw DuplicateEmailException(email);
            }

            await InvalidateAfterCustomerChangeAsync(customer.Id, ct);
            return customer.Id;


        }

        public async Task UpdateAsync(Guid id, UpdateCustomerDto dto, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");
            if (dto is null) throw new BadRequestAppException("Invalid payload.");

            var customerRepo = _unitOfWork.Repo<CustomerEntity>();
            var emailRepo = _unitOfWork.Repo<EmailEntity>(); // ✅ مهم

            var customer = await customerRepo.GetByIdAsync(
                id, ct: ct, asNoTracking: false, x => x.EmailAddresses);

            if (customer is null) throw new NotFoundAppException("Customer not found.");

            static string NormEmail(string? e) => (e ?? "").Trim().ToLowerInvariant();
            static string NormKind(string? k) => string.IsNullOrWhiteSpace(k) ? "business" : k.Trim().ToLowerInvariant();

            // ===== 1) تحديث بيانات العميل الأساسية =====
            var basic = dto.Customer ?? throw new BadRequestAppException("Missing customer data.");

            customer.Type = basic.Type;
            customer.FirstName = basic.FirstName;
            customer.LastName = basic.LastName;
            customer.Name = basic.Name ?? string.Empty;
            customer.Phone = string.IsNullOrWhiteSpace(basic.Phone) ? null : basic.Phone.Trim();
            customer.TaxId = string.IsNullOrWhiteSpace(basic.TaxId) ? null : basic.TaxId.Trim();
            customer.Notes = string.IsNullOrWhiteSpace(basic.Notes) ? null : basic.Notes.Trim();

            NormalizeAndValidateCustomer(customer);

            // ===== 2) incoming emails (مع Id اختياري) =====
            var incomingRaw = (basic.EmailAddresses ?? new List<EmailAddressDto>())
                .Select(x => new
                {
                    Id = (x.Id.HasValue && x.Id.Value != Guid.Empty) ? x.Id.Value : (Guid?)null,
                    Email = NormEmail(x.Email),
                    Kind = NormKind(x.Kind)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Email))
                .ToList();

            if (incomingRaw.Count == 0)
                throw new BadRequestAppException("At least one email address is required.");

            // منع تكرار الإيميل داخل نفس الطلب
            var dup = incomingRaw.GroupBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                                 .FirstOrDefault(g => g.Count() > 1);
            if (dup != null)
                throw new BadRequestAppException($"Duplicate email in request: {dup.Key}");

            // Global uniqueness, excluding the current customer so unchanged emails remain valid.
            await EnsureEmailsAreUniqueAsync(
                incomingRaw.Select(x => x.Email),
                exceptCustomerId: id,
                ct);

            // ===== 3) فهارس من DB =====
            var dbEmails = customer.EmailAddresses.ToList(); // tracked
            var dbById = dbEmails.ToDictionary(x => x.Id, x => x);
            var dbByEmail = dbEmails
                .Where(x => !string.IsNullOrWhiteSpace(NormEmail(x.Email)))
                .ToDictionary(x => NormEmail(x.Email), x => x, StringComparer.OrdinalIgnoreCase);

            var incomingEmailSet = new HashSet<string>(incomingRaw.Select(x => x.Email), StringComparer.OrdinalIgnoreCase);
            var keepIds = new HashSet<Guid>(); // كل اللي لازم يبقى

            // ===== 4) Update/Add بشكل مضمون =====
            foreach (var inc in incomingRaw)
            {
                // A) إذا جاء Id صحيح
                if (inc.Id.HasValue && dbById.TryGetValue(inc.Id.Value, out var dbRowById))
                {
                    if (NormEmail(dbRowById.Email) != inc.Email) dbRowById.Email = inc.Email;
                    if (NormKind(dbRowById.Kind) != inc.Kind) dbRowById.Kind = inc.Kind;

                    keepIds.Add(dbRowById.Id);
                    continue;
                }

                // B) إذا id ضاع من الفرونت، طابق على الإيميل (يحميك من حذف/إعادة إدراج غلط)
                if (dbByEmail.TryGetValue(inc.Email, out var dbRowByEmail))
                {
                    if (NormKind(dbRowByEmail.Kind) != inc.Kind) dbRowByEmail.Kind = inc.Kind;

                    keepIds.Add(dbRowByEmail.Id);
                    continue;
                }

                // C) جديد تمامًا => INSERT مضمون عبر repo
                var newEmail = new EmailEntity
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customer.Id,
                    Email = inc.Email,
                    Kind = inc.Kind
                };

                // ✅ هذا يضمن أن EF يعتبرها Added وليس Modified
                await emailRepo.AddAsync(newEmail, ct);

                // (اختياري) للمزامنة داخل الذاكرة
                customer.EmailAddresses.Add(newEmail);

                keepIds.Add(newEmail.Id);
            }

            // ===== 5) Delete: احذف أي DB row غير موجود في الطلب =====
            foreach (var db in dbEmails)
            {
                var dbEmail = NormEmail(db.Email);

                // احتفظ به لو جاء نفس Id أو نفس Email
                var keep = keepIds.Contains(db.Id) || incomingEmailSet.Contains(dbEmail);

                if (!keep)
                {
                    // ✅ حذف مضمون عبر repo (Deleted)
                    emailRepo.Remove(db);

                    // (اختياري) للمزامنة داخل الذاكرة
                    customer.EmailAddresses.Remove(db);
                }
            }

            // ===== 6) حفظ =====
            try
            {
                await _unitOfWork.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueEmailViolation(ex))
            {
                var email = GetEmailFromUpdateException(ex)
                            ?? incomingRaw.FirstOrDefault()?.Email
                            ?? string.Empty;

                _log.LogWarning(ex,
                    "Duplicate customer email rejected while updating {CustomerId}: {Email}",
                    id, email);

                throw DuplicateEmailException(email);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                foreach (var e in ex.Entries)
                {
                    var type = e.Metadata.ClrType.Name;
                    var state = e.State;
                    var pk = e.Properties.First(p => p.Metadata.IsPrimaryKey()).CurrentValue;

                    _log.LogError("Concurrency on {Type} pk={Pk} state={State}", type, pk, state);

                    var dbValues = await e.GetDatabaseValuesAsync(ct);
                    _log.LogError("DB values exist? {Exists}", dbValues != null);
                }
                throw;
            }

            await InvalidateAfterCustomerChangeAsync(id, ct);
            _log.LogInformation("Customer updated. {CustomerId}", id);
        }


        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");

            var repo = _unitOfWork.Repo<CustomerEntity>();
            var projectsRepo = _unitOfWork.Repo<Project>();

            // Final protection at the service layer. This also covers deletion calls
            // coming from places other than the Clients Razor Page.
            var projectCount = await projectsRepo.Query(asNoTracking: true)
                .CountAsync(x => x.CustomerId == id, ct);

            if (projectCount > 0)
            {
                throw new BadRequestAppException(
                    $"This client cannot be deleted because it is linked to {projectCount} project(s) and related project data.");
            }

            var entity = await repo.GetByIdAsync(id, ct: ct, asNoTracking: false);
            if (entity is null) return;

            repo.Remove(entity);
            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterCustomerChangeAsync(id, ct);

            _log.LogInformation("Customer deleted. {CustomerId}", id);
        }

        // =========================
        // Addresses 
        // =========================
        public async Task<Guid> CreateAddressAsync(CreateCustomerAddressDto dto, CancellationToken ct = default)
        {
            if (dto.CustomerId == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");

            var customersRepo = _unitOfWork.Repo<CustomerEntity>();
            var addressesRepo = _unitOfWork.Repo<AddressEntity>();

            var exists = await customersRepo.AnyAsync(x => x.Id == dto.CustomerId, ct);
            if (!exists) throw new NotFoundAppException("Customer not found.");

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                if (dto.Address.IsDefault)
                {
                    var current = await addressesRepo.ListAsync(a => a.CustomerId == dto.CustomerId, ct, asNoTracking: false);
                    foreach (var a in current.Where(a => a.IsDefault))
                    {
                        a.IsDefault = false;
                        addressesRepo.Update(a);
                    }
                }

                var hasAny = await addressesRepo.AnyAsync(a => a.CustomerId == dto.CustomerId, ct);

                var address = dto.Address.Adapt<AddressEntity>();
                address.CustomerId = dto.CustomerId;
                address.IsDefault = hasAny ? dto.Address.IsDefault : true;

                string? fallbackFullNameOrCompany = null;
                if (string.IsNullOrWhiteSpace(address.FullNameOrCompany))
                {
                    var customer = await customersRepo.FirstOrDefaultAsync(
                        x => x.Id == dto.CustomerId,
                        ct,
                        asNoTracking: true);
                    if (customer is null) throw new NotFoundAppException("Customer not found.");
                    fallbackFullNameOrCompany = customer.Name;
                }

                NormalizeAndValidateAddress(address, fallbackFullNameOrCompany);
                address.IsLexware = false;

                await addressesRepo.AddAsync(address, ct);
                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterCustomerChangeAsync(dto.CustomerId, ct);

                _log.LogInformation("Customer address created. {CustomerId} {AddressId}", dto.CustomerId, address.Id);
                return address.Id;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        public async Task UpdateAddressAsync(UpdateCustomerAddressDto dto, CancellationToken ct = default)
        {
            if (dto.CustomerId == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");
            if (dto.AddressId == Guid.Empty) throw new BadRequestAppException("Invalid address id.");

            var addressesRepo = _unitOfWork.Repo<AddressEntity>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var address = await addressesRepo.FirstOrDefaultAsync(
                    a => a.Id == dto.AddressId && a.CustomerId == dto.CustomerId,
                    ct,
                    asNoTracking: false);

                if (address is null) throw new NotFoundAppException("Address not found.");


                var normalizedAddress = dto.Address.Adapt<AddressEntity>();
                NormalizeAndValidateAddress(normalizedAddress);

                address.FullNameOrCompany = normalizedAddress.FullNameOrCompany;
                address.Country = normalizedAddress.Country;
                address.City = normalizedAddress.City;
                address.PostalCode = normalizedAddress.PostalCode;
                address.AddressLine2 = normalizedAddress.AddressLine2;
                address.Label = normalizedAddress.Label;
                address.CountryCode = normalizedAddress.CountryCode;
                address.StreetRaw = normalizedAddress.StreetRaw;

                if (dto.Address.IsDefault && !address.IsDefault)
                {
                    var current = await addressesRepo.ListAsync(a => a.CustomerId == dto.CustomerId, ct, asNoTracking: false);
                    foreach (var a in current.Where(a => a.IsDefault))
                    {
                        a.IsDefault = false;
                        addressesRepo.Update(a);
                    }
                    address.IsDefault = true;
                }

                addressesRepo.Update(address);
                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterCustomerChangeAsync(dto.CustomerId, ct);

                _log.LogInformation("Customer address updated. {CustomerId} {AddressId}", dto.CustomerId, dto.AddressId);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        public async Task DeleteAddressAsync(DeleteCustomerAddressDto dto, CancellationToken ct = default)
        {
            if (dto.CustomerId == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");
            if (dto.AddressId == Guid.Empty) throw new BadRequestAppException("Invalid address id.");

            var addressesRepo = _unitOfWork.Repo<AddressEntity>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var addresses = await addressesRepo.ListAsync(
                    a => a.CustomerId == dto.CustomerId,
                    ct,
                    asNoTracking: false);

                if (addresses.Count == 0) return;

                var address = addresses.FirstOrDefault(a => a.Id == dto.AddressId);
                if (address is null) return;

                if (addresses.Count == 1)
                    throw new BadRequestAppException("Cannot delete the last location.");

                if (address.IsDefault)
                    throw new BadRequestAppException("Cannot delete the default location. Please choose another default location first.");

                addressesRepo.Remove(address);
                await _unitOfWork.SaveChangesAsync(ct);

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterCustomerChangeAsync(dto.CustomerId, ct);

                _log.LogInformation("Customer address deleted. {CustomerId} {AddressId}", dto.CustomerId, dto.AddressId);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }
        public async Task SetDefaultAddressAsync(SetDefaultCustomerAddressDto dto, CancellationToken ct = default)
        {
            if (dto.CustomerId == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");
            if (dto.AddressId == Guid.Empty) throw new BadRequestAppException("Invalid address id.");

            var addressesRepo = _unitOfWork.Repo<AddressEntity>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var addresses = await addressesRepo.ListAsync(a => a.CustomerId == dto.CustomerId, ct, asNoTracking: false);
                if (addresses.Count == 0) throw new NotFoundAppException("No addresses found.");

                var target = addresses.FirstOrDefault(a => a.Id == dto.AddressId);
                if (target is null) throw new NotFoundAppException("Address not found.");

                foreach (var a in addresses)
                {
                    var shouldBeDefault = a.Id == dto.AddressId;
                    if (a.IsDefault != shouldBeDefault)
                    {
                        a.IsDefault = shouldBeDefault;
                        addressesRepo.Update(a);
                    }
                }

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterCustomerChangeAsync(dto.CustomerId, ct);

                _log.LogInformation("Default address set. {CustomerId} {AddressId}", dto.CustomerId, dto.AddressId);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        // =========================
        // Contacts
        // =========================
        public async Task<Guid> CreateContactAsync(CreateCustomerContactDto dto, CancellationToken ct = default)
        {
            if (dto.CustomerId == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");

            if (dto.Contact is null)
                throw new BadRequestAppException("Company contact is required.");

            var customersRepo = _unitOfWork.Repo<CustomerEntity>();
            var contactsRepo = _unitOfWork.Repo<ContactEntity>();

            var customer = await customersRepo.FirstOrDefaultAsync(
                x => x.Id == dto.CustomerId,
                ct,
                asNoTracking: true);

            if (customer is null)
                throw new NotFoundAppException("Customer not found.");

            if (customer.Type != CustomerType.Company)
                throw new BadRequestAppException("Contacts are available for company clients only.");

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var hasAny = await contactsRepo.AnyAsync(c => c.CustomerId == dto.CustomerId, ct);

                if (dto.Contact.IsPrimary)
                {
                    var current = await contactsRepo.ListAsync(c => c.CustomerId == dto.CustomerId, ct, asNoTracking: false);
                    foreach (var c in current.Where(c => c.IsPrimary))
                    {
                        c.IsPrimary = false;
                        contactsRepo.Update(c);
                    }
                }

                var contact = dto.Contact.Adapt<ContactEntity>();
                NormalizeAndValidateCompanyContact(contact);

                contact.CustomerId = dto.CustomerId;
                contact.IsLexware = false;
                contact.IsPrimary = hasAny ? dto.Contact.IsPrimary : true;

                await contactsRepo.AddAsync(contact, ct);
                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterCustomerChangeAsync(dto.CustomerId, ct);

                _log.LogInformation("Customer contact created. {CustomerId} {ContactId}", dto.CustomerId, contact.Id);
                return contact.Id;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        public async Task UpdateContactAsync(UpdateCustomerContactDto dto, CancellationToken ct = default)
        {
            if (dto.CustomerId == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");
            if (dto.ContactId == Guid.Empty) throw new BadRequestAppException("Invalid contact id.");
            if (dto.Contact is null) throw new BadRequestAppException("Company contact is required.");

            var customersRepo = _unitOfWork.Repo<CustomerEntity>();
            var contactsRepo = _unitOfWork.Repo<ContactEntity>();

            var customer = await customersRepo.FirstOrDefaultAsync(
                x => x.Id == dto.CustomerId,
                ct,
                asNoTracking: true);

            if (customer is null)
                throw new NotFoundAppException("Customer not found.");

            if (customer.Type != CustomerType.Company)
                throw new BadRequestAppException("Contacts are available for company clients only.");

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var contact = await contactsRepo.FirstOrDefaultAsync(
                    c => c.Id == dto.ContactId && c.CustomerId == dto.CustomerId,
                    ct,
                    asNoTracking: false);

                if (contact is null) throw new NotFoundAppException("Contact not found.");

                contact.Name = dto.Contact.Name;
                contact.Salutation = dto.Contact.Salutation;
                contact.FirstName = dto.Contact.FirstName;
                contact.LastName = dto.Contact.LastName;
                contact.Position = dto.Contact.Position;
                contact.Email = dto.Contact.Email;
                contact.Phone = dto.Contact.Phone;

                NormalizeAndValidateCompanyContact(contact);

                if (dto.Contact.IsPrimary && !contact.IsPrimary)
                {
                    var current = await contactsRepo.ListAsync(c => c.CustomerId == dto.CustomerId, ct, asNoTracking: false);
                    foreach (var c in current.Where(c => c.IsPrimary))
                    {
                        c.IsPrimary = false;
                        contactsRepo.Update(c);
                    }
                    contact.IsPrimary = true;
                }

                contactsRepo.Update(contact);
                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterCustomerChangeAsync(dto.CustomerId, ct);

                _log.LogInformation("Customer contact updated. {CustomerId} {ContactId}", dto.CustomerId, dto.ContactId);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        public async Task DeleteContactAsync(DeleteCustomerContactDto dto, CancellationToken ct = default)
        {
            if (dto.CustomerId == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");
            if (dto.ContactId == Guid.Empty) throw new BadRequestAppException("Invalid contact id.");

            var contactsRepo = _unitOfWork.Repo<ContactEntity>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var contact = await contactsRepo.FirstOrDefaultAsync(
                    c => c.Id == dto.ContactId && c.CustomerId == dto.CustomerId,
                    ct,
                    asNoTracking: false);

                if (contact is null) return;

                var wasPrimary = contact.IsPrimary;

                contactsRepo.Remove(contact);
                await _unitOfWork.SaveChangesAsync(ct);

                if (wasPrimary)
                {
                    var remaining = await contactsRepo.ListAsync(c => c.CustomerId == dto.CustomerId, ct, asNoTracking: false);
                    var next = remaining.OrderBy(c => c.Id).FirstOrDefault();
                    if (next is not null)
                    {
                        next.IsPrimary = true;
                        contactsRepo.Update(next);
                        await _unitOfWork.SaveChangesAsync(ct);
                    }
                }

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterCustomerChangeAsync(dto.CustomerId, ct);

                _log.LogInformation("Customer contact deleted. {CustomerId} {ContactId}", dto.CustomerId, dto.ContactId);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        public async Task SetPrimaryContactAsync(SetPrimaryCustomerContactDto dto, CancellationToken ct = default)
        {
            if (dto.CustomerId == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");
            if (dto.ContactId == Guid.Empty) throw new BadRequestAppException("Invalid contact id.");

            var contactsRepo = _unitOfWork.Repo<ContactEntity>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var contacts = await contactsRepo.ListAsync(c => c.CustomerId == dto.CustomerId, ct, asNoTracking: false);
                if (contacts.Count == 0) throw new NotFoundAppException("No contacts found.");

                var target = contacts.FirstOrDefault(c => c.Id == dto.ContactId);
                if (target is null) throw new NotFoundAppException("Contact not found.");

                foreach (var c in contacts)
                {
                    var shouldBePrimary = c.Id == dto.ContactId;
                    if (c.IsPrimary != shouldBePrimary)
                    {
                        c.IsPrimary = shouldBePrimary;
                        contactsRepo.Update(c);
                    }
                }

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterCustomerChangeAsync(dto.CustomerId, ct);

                _log.LogInformation("Primary contact set. {CustomerId} {ContactId}", dto.CustomerId, dto.ContactId);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        private static void NormalizeAndValidateCustomer(CustomerEntity customer)
        {
            if (customer is null)
                throw new BadRequestAppException("Customer is required.");

            customer.FirstName = string.IsNullOrWhiteSpace(customer.FirstName)
                ? null
                : customer.FirstName.Trim();

            customer.LastName = string.IsNullOrWhiteSpace(customer.LastName)
                ? null
                : customer.LastName.Trim();

            customer.Name = (customer.Name ?? string.Empty).Trim();

            switch (customer.Type)
            {
                case CustomerType.Individual:
                    if (string.IsNullOrWhiteSpace(customer.FirstName))
                        throw new BadRequestAppException("First name is required.");

                    if (string.IsNullOrWhiteSpace(customer.LastName))
                        throw new BadRequestAppException("Last name is required.");

                    customer.Name = $"{customer.FirstName} {customer.LastName}".Trim();
                    break;

                case CustomerType.Company:
                    if (string.IsNullOrWhiteSpace(customer.Name))
                        throw new BadRequestAppException("Company name is required.");

                    // Never persist stale individual-name values on a company.
                    customer.FirstName = null;
                    customer.LastName = null;
                    break;

                default:
                    throw new BadRequestAppException("Invalid customer type.");
            }
        }

        private static void NormalizeAndValidateCompanyContact(ContactEntity contact)
        {
            if (contact is null)
                throw new BadRequestAppException("Company contact is required.");

            contact.Name = (contact.Name ?? string.Empty).Trim();
            contact.Salutation = (contact.Salutation ?? string.Empty).Trim();
            contact.FirstName = (contact.FirstName ?? string.Empty).Trim();
            contact.LastName = (contact.LastName ?? string.Empty).Trim();
            contact.Position = (contact.Position ?? string.Empty).Trim();
            contact.Email = (contact.Email ?? string.Empty).Trim().ToLowerInvariant();
            contact.Phone = (contact.Phone ?? string.Empty).Trim();

            // The create modal has one Contact.Name field, while the separate
            // contact editor has FirstName and LastName fields. Support both shapes.
            if (!string.IsNullOrWhiteSpace(contact.FirstName)
                && !string.IsNullOrWhiteSpace(contact.LastName))
            {
                contact.Name = $"{contact.FirstName} {contact.LastName}".Trim();
            }

            var missingFields = new List<string>();

            if (string.IsNullOrWhiteSpace(contact.Name)) missingFields.Add("Name");
            if (string.IsNullOrWhiteSpace(contact.Position)) missingFields.Add("Position");
            if (string.IsNullOrWhiteSpace(contact.Email)) missingFields.Add("Email");
            if (string.IsNullOrWhiteSpace(contact.Phone)) missingFields.Add("Phone");

            if (missingFields.Count > 0)
            {
                throw new BadRequestAppException(
                    $"All company contact fields are required. Missing: {string.Join(", ", missingFields)}");
            }

            // Salutation is optional. FirstName/LastName may remain empty when the
            // create modal supplies the contact's full name in Contact.Name.
        }

        private static void NormalizeAndValidateAddress(
            AddressEntity address,
            string? fallbackFullNameOrCompany = null)
        {
            if (address is null)
                throw new BadRequestAppException("Address is required.");

            address.FullNameOrCompany = string.IsNullOrWhiteSpace(address.FullNameOrCompany)
                ? (fallbackFullNameOrCompany ?? string.Empty).Trim()
                : address.FullNameOrCompany.Trim();
            address.Label = (address.Label ?? string.Empty).Trim();
            address.StreetRaw = (address.StreetRaw ?? string.Empty).Trim();
            address.AddressLine2 = (address.AddressLine2 ?? string.Empty).Trim();
            address.PostalCode = (address.PostalCode ?? string.Empty).Trim();
            address.City = (address.City ?? string.Empty).Trim();
            address.Country = (address.Country ?? string.Empty).Trim();
            address.CountryCode = (address.CountryCode ?? string.Empty).Trim().ToUpperInvariant();

            var missingFields = new List<string>();

            if (string.IsNullOrWhiteSpace(address.FullNameOrCompany)) missingFields.Add("FullNameOrCompany");
            if (string.IsNullOrWhiteSpace(address.Label)) missingFields.Add("Label");
            if (string.IsNullOrWhiteSpace(address.StreetRaw)) missingFields.Add("StreetRaw");
            if (string.IsNullOrWhiteSpace(address.AddressLine2)) missingFields.Add("AddressLine2");
            if (string.IsNullOrWhiteSpace(address.PostalCode)) missingFields.Add("PostalCode");
            if (string.IsNullOrWhiteSpace(address.City)) missingFields.Add("City");
            if (string.IsNullOrWhiteSpace(address.Country)) missingFields.Add("Country");
            if (string.IsNullOrWhiteSpace(address.CountryCode)) missingFields.Add("CountryCode");

            if (missingFields.Count > 0)
            {
                throw new BadRequestAppException(
                    $"All address fields are required. Missing: {string.Join(", ", missingFields)}");
            }
        }

        // =========================
        // Cache helpers
        // =========================
        private async Task InvalidateAfterCustomerChangeAsync(Guid customerId, CancellationToken ct)
        {
            await _cache.RemoveAsync(CustomerCacheKeys.Details(customerId), ct);
            await _cache.BumpVersionAsync(CustomerCacheKeys.ListVersionKey, ct);
        }
        static string NormEmail(string? e) => (e ?? "").Trim().ToLowerInvariant();
        static string NormKind(string? k) => string.IsNullOrWhiteSpace(k) ? "business" : k.Trim().ToLowerInvariant();

        private async Task EnsureEmailsAreUniqueAsync(
            IEnumerable<string> emails,
            Guid? exceptCustomerId,
            CancellationToken ct)
        {
            var normalized = emails
                .Select(NormEmail)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalized.Count == 0)
                return;

            var emailRepo = _unitOfWork.Repo<EmailEntity>();
            var query = emailRepo.Query(asNoTracking: true)
                .Where(x => normalized.Contains(x.Email.Trim().ToLower()));

            if (exceptCustomerId.HasValue)
                query = query.Where(x => x.CustomerId != exceptCustomerId.Value);

            var existingEmail = await query
                .Select(x => x.Email)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(existingEmail))
                throw DuplicateEmailException(existingEmail);
        }

        private static BadRequestAppException DuplicateEmailException(string? email)
        {
            return new BadRequestAppException(
                $"{DuplicateEmailMessagePrefix}{NormEmail(email)}");
        }

        private static bool IsUniqueEmailViolation(DbUpdateException ex)
        {
            var text = ex.ToString();

            return text.Contains(
                       "UX_CustomerEmailAddresses_Email",
                       StringComparison.OrdinalIgnoreCase)
                   || (text.Contains("CustomerEmailAddresses", StringComparison.OrdinalIgnoreCase)
                       && (text.Contains("unique", StringComparison.OrdinalIgnoreCase)
                           || text.Contains("duplicate", StringComparison.OrdinalIgnoreCase)));
        }

        private static string? GetEmailFromUpdateException(DbUpdateException ex)
        {
            return ex.Entries
                .Select(entry => entry.Entity)
                .OfType<EmailEntity>()
                .Select(entity => NormEmail(entity.Email))
                .FirstOrDefault(email => !string.IsNullOrWhiteSpace(email));
        }

        static int KindRank(string kind) => kind switch
        {
            "business" => 3,
            "other" => 2,
            "private" => 1,
            _ => 0
        };

        static List<(string Email, string Kind)> DistinctEmails(IEnumerable<(string Email, string Kind)> src)
        {
            return src
                .Select(x => (Email: NormEmail(x.Email), Kind: NormKind(x.Kind)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Email))
                .GroupBy(x => x.Email) // dedupe على الإيميل
                .Select(g => g.OrderByDescending(x => KindRank(x.Kind)).First()) // اختار الأفضل
                .ToList();
        }
        public async Task<List<CustomerProjectItemView>> GetCustomerProjectsAsync(Guid customerId, CancellationToken ct = default)
        {
            if (customerId == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");

            var projectsRepo = _unitOfWork.Repo<Project>(); // غيّر ProjectEntity حسب اسم الـ Entity عندك

            var items = await projectsRepo.Query(asNoTracking: true)
                .Where(p => p.CustomerId == customerId)
                .OrderByDescending(p => p.CreatedAt) // أو StartDate
                .Select(p => new CustomerProjectItemView
                {
                    Id = p.Id,
                    Title = p.Title,
                    Status = p.Status,
                    StartDate = p.StartDate,
                    EndDate = p.EndDate
                })
                .ToListAsync(ct);

            return items;
        }
    }
}
