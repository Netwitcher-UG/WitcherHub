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
using static WitcherHub.Infrastructure.Data.Models.Enums;

using AddressEntity = WitcherHub.Infrastructure.Data.Models.CustomerAddress;
using ContactEntity = WitcherHub.Infrastructure.Data.Models.CustomerContact;
using CustomerEntity = WitcherHub.Infrastructure.Data.Models.Customer;

namespace WitcherHub.Infrastructure.ManageData.Customers
{
    public sealed class Customer : ICustomer
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppCache _cache;
        private readonly ILogger<Customer> _log;

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

        public Customer(IUnitOfWork unitOfWork, IAppCache cache, ILogger<Customer> log)
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
            var cacheKey = CustomerCacheKeys.ListWithVersion(page, pageSize, search, version);

            return await _cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    var repo = _unitOfWork.Repo<CustomerEntity>();

                    // DB-side query (fast)
                    var q = repo.Query(asNoTracking: true);

                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        var s = search.Trim();
                        var escaped = EscapeLike(s);
                        var pattern = $"%{escaped}%";

                        // SQL Server safe LIKE with escape char '!'
                        q = q.Where(c =>
                            EF.Functions.Like(c.Name, pattern, "!") ||
                            (c.Email != null && EF.Functions.Like(c.Email, pattern, "!")) ||
                            (c.Phone != null && EF.Functions.Like(c.Phone, pattern, "!")) ||
                            (c.TaxId != null && EF.Functions.Like(c.TaxId, pattern, "!")));
                    }

                    var total = await q.LongCountAsync(token);
                    if (total == 0)
                        return PagedResult<CustomerViews.CustomerListItemView>.Empty(page, pageSize);

                    var items = await q
                        .OrderBy(c => c.Name)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .Select(c => new CustomerViews.CustomerListItemView
                        {
                            Id = c.Id,
                            Type = c.Type,
                            Name = c.Name,
                            Email = c.Email,
                            Phone = c.Phone,
                            TaxId = c.TaxId,

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
                        x => x.Contacts);

                    if (entity is null)
                        return null;

                    return new CustomerViews.CustomerDetailsView
                    {
                        Id = entity.Id,
                        Type = entity.Type,
                        Name = entity.Name,
                        Email = entity.Email,
                        Phone = entity.Phone,
                        TaxId = entity.TaxId,
                        Notes = entity.Notes,

                        Addresses = entity.Addresses
                            .OrderByDescending(a => a.IsDefault)
                            .ThenBy(a => a.Label)
                            .Select(a => new CustomerViews.CustomerAddressItemView
                            {
                                Id = a.Id,
                                Label = a.Label,
                                Country = a.Country,
                                City = a.City,
                                PostalCode = a.PostalCode,
                                FullNameOrCompany = a.FullNameOrCompany,
                                Street = a.Street,
                                StreetNr = a.StreetNr,
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

            var customer = dto.Customer.Adapt<CustomerEntity>();

            // Address required
            var address = dto.Address.Adapt<AddressEntity>();
            address.IsDefault = true;
            customer.Addresses.Add(address);

            // Contact only for Company
            if (dto.Customer.Type == CustomerType.Company)
            {
                var contact = dto.Contact.Adapt<ContactEntity>();
                contact.IsPrimary = true;
                customer.Contacts.Add(contact);
            }

            await customerRepo.AddAsync(customer, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterCustomerChangeAsync(customer.Id, ct);

            _log.LogInformation("Customer created. {CustomerId}", customer.Id);
            return customer.Id;
        }

        public async Task UpdateAsync(Guid id, UpdateCustomerDto dto, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");
            if (dto is null) throw new BadRequestAppException("Invalid payload.");

            var repo = _unitOfWork.Repo<CustomerEntity>();

            var entity = await repo.GetByIdAsync(id, ct: ct, asNoTracking: false);
            if (entity is null) throw new NotFoundAppException("Customer not found.");

            var basic = dto.Customer?.Customer ?? throw new BadRequestAppException("Missing customer data.");

            entity.Type = basic.Type;
            entity.Name = (basic.Name ?? "").Trim();
            entity.Email = string.IsNullOrWhiteSpace(basic.Email) ? null : basic.Email.Trim();
            entity.Phone = string.IsNullOrWhiteSpace(basic.Phone) ? null : basic.Phone.Trim();
            entity.TaxId = string.IsNullOrWhiteSpace(basic.TaxId) ? null : basic.TaxId.Trim();
            entity.Notes = string.IsNullOrWhiteSpace(basic.Notes) ? null : basic.Notes.Trim();

            repo.Update(entity);
            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterCustomerChangeAsync(id, ct);

            _log.LogInformation("Customer updated. {CustomerId}", id);
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");

            var repo = _unitOfWork.Repo<CustomerEntity>();

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

                address.Label = dto.Address.Label;
                address.Country = dto.Address.Country;
                address.City = dto.Address.City;
                address.PostalCode = dto.Address.PostalCode;

                // required fields on entity
                address.FullNameOrCompany = dto.Address.FullNameOrCompany ?? address.FullNameOrCompany;
                address.Street = dto.Address.Street ?? address.Street;
                address.StreetNr = dto.Address.StreetNr ?? address.StreetNr;

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
                var address = await addressesRepo.FirstOrDefaultAsync(
                    a => a.Id == dto.AddressId && a.CustomerId == dto.CustomerId,
                    ct,
                    asNoTracking: false);

                if (address is null) return;

                var wasDefault = address.IsDefault;

                addressesRepo.Remove(address);
                await _unitOfWork.SaveChangesAsync(ct);

                if (wasDefault)
                {
                    var remaining = await addressesRepo.ListAsync(a => a.CustomerId == dto.CustomerId, ct, asNoTracking: false);
                    var next = remaining.OrderBy(a => a.Id).FirstOrDefault();
                    if (next is not null)
                    {
                        next.IsDefault = true;
                        addressesRepo.Update(next);
                        await _unitOfWork.SaveChangesAsync(ct);
                    }
                }

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

            var customersRepo = _unitOfWork.Repo<CustomerEntity>();
            var contactsRepo = _unitOfWork.Repo<ContactEntity>();

            var exists = await customersRepo.AnyAsync(x => x.Id == dto.CustomerId, ct);
            if (!exists) throw new NotFoundAppException("Customer not found.");

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
                contact.CustomerId = dto.CustomerId;
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

            var contactsRepo = _unitOfWork.Repo<ContactEntity>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var contact = await contactsRepo.FirstOrDefaultAsync(
                    c => c.Id == dto.ContactId && c.CustomerId == dto.CustomerId,
                    ct,
                    asNoTracking: false);

                if (contact is null) throw new NotFoundAppException("Contact not found.");

                contact.Name = dto.Contact.Name ?? contact.Name;
                contact.Position = dto.Contact.Position;
                contact.Email = dto.Contact.Email;
                contact.Phone = dto.Contact.Phone;

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

        // =========================
        // Cache helpers
        // =========================
        private async Task InvalidateAfterCustomerChangeAsync(Guid customerId, CancellationToken ct)
        {
            await _cache.RemoveAsync(CustomerCacheKeys.Details(customerId), ct);
            await _cache.BumpVersionAsync(CustomerCacheKeys.ListVersionKey, ct);
        }
    }
}
