using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WitcherHub.Application.Common.CacheKeys;
using WitcherHub.Application.Common.Caching;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Invoices;
using WitcherHub.Application.Models.View.Invoices;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.ManageData.Invoices
{
    public sealed class ManageInvoice : IInvoice
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppCache _cache;
        private readonly ILogger<ManageInvoice> _log;

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

        public ManageInvoice(IUnitOfWork unitOfWork, IAppCache cache, ILogger<ManageInvoice> log)
        {
            _unitOfWork = unitOfWork;
            _cache = cache;
            _log = log;
        }

        // =========================
        // LIST (By Project)
        // =========================
        public async Task<PagedResult<InvoiceViews.InvoiceListItemView>> GetInvoicesByProjectAsync(
            Guid projectId,
            int page = 1,
            int pageSize = 10,
            string? search = null,
            CancellationToken ct = default)
        {
            if (projectId == Guid.Empty) throw new BadRequestAppException("Invalid project id.");

            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 200 ? 10 : pageSize;

            var version = await _cache.GetOrCreateVersionAsync(InvoiceCacheKeys.ListVersionKey, ct);
            var cacheKey = InvoiceCacheKeys.ListByProjectWithVersion(page, pageSize, projectId, search, version);

            return await _cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    var repo = _unitOfWork.Repo<Invoice>();
                    var q = repo.Query(asNoTracking: true)
                        .Where(x => x.ProjectId == projectId);

                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        var s = search.Trim();
                        var escaped = EscapeLike(s);
                        var pattern = $"%{escaped}%";

                        q = q.Where(x =>
                            EF.Functions.Like(x.InvoiceNo, pattern, "!") ||
                            (x.Notes != null && EF.Functions.Like(x.Notes, pattern, "!")) ||
                            x.Items.Any(i => EF.Functions.Like(i.Title, pattern, "!"))
                        );
                    }

                    var total = await q.LongCountAsync(token);
                    if (total == 0)
                        return PagedResult<InvoiceViews.InvoiceListItemView>.Empty(page, pageSize);

                    var items = await q
                        .OrderByDescending(x => x.CreatedAt)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .Select(x => new InvoiceViews.InvoiceListItemView
                        {
                            Id = x.Id,
                            ProjectId = x.ProjectId,
                            ContractId = x.ContractId,
                            InvoiceNo = x.InvoiceNo,
                            Status = x.Status,
                            Currency = x.Currency,
                            CreatedAt = x.CreatedAt,
                            IssueDate = x.IssueDate,
                            DueDate = x.DueDate,
                         
                            LexwareVoucherStatus = x.LexwareVoucherStatus,
                            LexwareSyncedAt = x.LexwareSyncedAt,

                            ItemsTotal = x.Items.Sum(i => i.Quantity * i.UnitPrice),
                            Total = x.Totals != null ? x.Totals.Total : x.Items.Sum(i => i.Quantity * i.UnitPrice),
                            BalanceDue = x.Totals != null ? x.Totals.BalanceDue : (x.Items.Sum(i => i.Quantity * i.UnitPrice))
                        })
                        .ToListAsync(token);

                    return new PagedResult<InvoiceViews.InvoiceListItemView>
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
        // DETAILS
        // =========================
        public async Task<InvoiceViews.InvoiceDetailsView?> GetInvoiceAsync(Guid id, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid invoice id.");

            var cacheKey = InvoiceCacheKeys.Details(id);

            return await _cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    var repo = _unitOfWork.Repo<Invoice>();

                    var entity = await repo.Query(asNoTracking: true)
                        .Where(x => x.Id == id)
                        .Select(x => new InvoiceViews.InvoiceDetailsView
                        {
                            Id = x.Id,
                            ProjectId = x.ProjectId,
                            ContractId = x.ContractId,

                            InvoiceNo = x.InvoiceNo,
                            Status = x.Status,
                            Currency = x.Currency,

                            Notes = x.Notes,
                            CreatedAt = x.CreatedAt,

                            IssueDate = x.IssueDate,
                            DueDate = x.DueDate,
                            IssuedAt = x.IssuedAt,
                            PaidAt = x.PaidAt,

                            TaxRateId = x.TaxRateId,
                            TaxName = x.TaxRate != null ? x.TaxRate.Name : null,

                            InvoiceDiscountType = x.InvoiceDiscountType,
                            InvoiceDiscountValue = x.InvoiceDiscountValue,
                            LexwareInvoiceId = x.LexwareInvoiceId,
                            LexwareVoucherNumber = x.LexwareVoucherNumber,
                            LexwareVoucherStatus = x.LexwareVoucherStatus,
                            LexwareResourceUri = x.LexwareResourceUri,
                            LexwareVersion = x.LexwareVersion,
                            LexwareSyncedAt = x.LexwareSyncedAt,
                            LexwarePdfPath = x.LexwarePdfPath,
                            LexwareSnapshot = x.LexwareSnapshot,
                          
                            Totals = x.Totals == null ? null : new InvoiceViews.InvoiceTotalsView
                            {
                                Subtotal = x.Totals.Subtotal,
                                DiscountTotal = x.Totals.DiscountTotal,
                                TaxTotal = x.Totals.TaxTotal,
                                Total = x.Totals.Total,
                                PaidTotal = x.Totals.PaidTotal,
                                BalanceDue = x.Totals.BalanceDue,
                                UpdatedAt = x.Totals.UpdatedAt
                            },

                            Items = x.Items
                                .OrderBy(i => i.Position)
                                .ThenBy(i => i.CreatedAt)
                                .Select(i => new InvoiceViews.InvoiceItemItemView
                                {
                                    Id = i.Id,
                                    ServiceId = i.ServiceId,
                                    ServiceName = i.Service != null ? i.Service.Name : null,

                                    Title = i.Title,
                                    Quantity = i.Quantity,
                                    UnitPrice = i.UnitPrice,

                                    Config = i.Config,
                                    PriceBreakdown = i.PriceBreakdown,

                                    TaxRateId = i.TaxRateId,
                                    TaxName = i.TaxRate != null ? i.TaxRate.Name : null,

                                    DiscountType = i.DiscountType,
                                    DiscountValue = i.DiscountValue,

                                    Position = i.Position,

                                    LineTotal = CalcLineTotal(i.Quantity, i.UnitPrice, i.DiscountType, i.DiscountValue)
                                })
                                .ToList()
                        })
                        .FirstOrDefaultAsync(token);

                    return entity;
                },
                DetailsCacheOptions,
                ct);
        }

        // =========================
        // CREATE INVOICE
        // =========================
        public async Task<Guid> CreateAsync(InvoiceDTOs dto, CancellationToken ct = default)
        {
            EnsureManualInvoicesEnabled();
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.Invoice.ProjectId == Guid.Empty) throw new BadRequestAppException("Invalid project id.");

            var projectsRepo = _unitOfWork.Repo<Project>();
            var invoicesRepo = _unitOfWork.Repo<Invoice>();

            var projectExists = await projectsRepo.AnyAsync(x => x.Id == dto.Invoice.ProjectId, ct);
            if (!projectExists) throw new NotFoundAppException("Project not found.");

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var invoiceNo = await GenerateInvoiceNoAsync(invoicesRepo, ct);

                var invoice = new Invoice
                {
                    ProjectId = dto.Invoice.ProjectId,
                    ContractId = dto.Invoice.ContractId,

                    InvoiceNo = invoiceNo,

                    Status = dto.Invoice.Status,
                    Currency = (dto.Invoice.Currency ?? "EUR").Trim(),

                    Notes = string.IsNullOrWhiteSpace(dto.Invoice.Notes) ? null : dto.Invoice.Notes.Trim(),

                    IssueDate = dto.Invoice.IssueDate,
                    DueDate = dto.Invoice.DueDate,

                 
                    IssuedAt = dto.Invoice.IssuedAt?.ToUniversalTime(),
                    PaidAt = dto.Invoice.PaidAt?.ToUniversalTime(),


                    InvoiceDiscountType = dto.Invoice.InvoiceDiscountType,
                    InvoiceDiscountValue = dto.Invoice.InvoiceDiscountValue,

                    TaxRateId = dto.Invoice.TaxRateId
                };

                // Items (optional)
                if (dto.Items is not null && dto.Items.Count > 0)
                {
                    int pos = 1;
                    foreach (var it in dto.Items.OrderBy(x => x.Position <= 0 ? int.MaxValue : x.Position))
                    {
                        var itemDto = new InvoiceItemDto
                        {
                            ServiceId = it.ServiceId,
                            Title = it.Title,
                            Quantity = it.Quantity,
                            UnitPrice = it.UnitPrice,
                            Config = it.Config,
                            TaxRateId = it.TaxRateId,
                            DiscountType = it.DiscountType,
                            DiscountValue = it.DiscountValue,
                            Position = it.Position > 0 ? it.Position : pos
                        };

                        var item = new InvoiceItem
                        {
                            Title = (it.Title ?? "").Trim(),
                            ServiceId = it.ServiceId,
                            Quantity = it.Quantity,
                            UnitPrice = it.UnitPrice,
                            Config = it.Config ?? JsonDocument.Parse("{}"),
                            TaxRateId = it.TaxRateId,
                            DiscountType = it.DiscountType,
                            DiscountValue = it.DiscountValue,
                            Position = it.Position > 0 ? it.Position : pos
                        };

                        item.PriceBreakdown = await BuildBreakdownAsync(itemDto, invoice.TaxRateId, ct);

                        invoice.Items.Add(item);
                        pos++;
                    }
                }

                await invoicesRepo.AddAsync(invoice, ct);
                await _unitOfWork.SaveChangesAsync(ct);

                await RecalculateInvoiceTotalsAsync(invoice.Id, ct);

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterInvoiceChangeAsync(invoice.Id, ct);

                _log.LogInformation("Invoice created. {InvoiceId} {InvoiceNo}", invoice.Id, invoice.InvoiceNo);
                return invoice.Id;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        // =========================
        // UPDATE INVOICE (header only + optional replace items)
        // =========================
        public async Task UpdateAsync(Guid id, UpdateInvoiceDto dto, CancellationToken ct = default)
        {
            EnsureManualInvoicesEnabled();
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid invoice id.");
            if (dto is null) throw new BadRequestAppException("Invalid payload.");

            var invoicesRepo = _unitOfWork.Repo<Invoice>();

            var invoice = await invoicesRepo.Query(asNoTracking: false)
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (invoice is null) throw new NotFoundAppException("Invoice not found.");

            invoice.Currency = (dto.Invoice.Currency ?? invoice.Currency ?? "EUR").Trim();
            invoice.Notes = string.IsNullOrWhiteSpace(dto.Invoice.Notes) ? null : dto.Invoice.Notes.Trim();

            invoice.ContractId = dto.Invoice.ContractId;
            invoice.IssueDate = dto.Invoice.IssueDate;
            invoice.DueDate = dto.Invoice.DueDate;
            invoice.IssuedAt = dto.Invoice.IssuedAt?.ToUniversalTime();
            invoice.PaidAt = dto.Invoice.PaidAt?.ToUniversalTime();


            invoice.InvoiceDiscountType = dto.Invoice.InvoiceDiscountType;
            invoice.InvoiceDiscountValue = dto.Invoice.InvoiceDiscountValue;
            invoice.TaxRateId = dto.Invoice.TaxRateId;

            invoice.Status = dto.Invoice.Status;

            if (dto.Items is not null)
            {
                invoice.Items.Clear();

                int pos = 1;
                foreach (var it in dto.Items.OrderBy(x => x.Position <= 0 ? int.MaxValue : x.Position))
                {
                    var itemDto = new InvoiceItemDto
                    {
                        ServiceId = it.ServiceId,
                        Title = it.Title,
                        Quantity = it.Quantity,
                        UnitPrice = it.UnitPrice,
                        Config = it.Config,
                        TaxRateId = it.TaxRateId,
                        DiscountType = it.DiscountType,
                        DiscountValue = it.DiscountValue,
                        Position = it.Position > 0 ? it.Position : pos
                    };

                    var item = new InvoiceItem
                    {
                        InvoiceId = invoice.Id,
                        Title = (it.Title ?? "").Trim(),
                        ServiceId = it.ServiceId,
                        Quantity = it.Quantity,
                        UnitPrice = it.UnitPrice,
                        Config = it.Config ?? JsonDocument.Parse("{}"),
                        TaxRateId = it.TaxRateId,
                        DiscountType = it.DiscountType,
                        DiscountValue = it.DiscountValue,
                        Position = it.Position > 0 ? it.Position : pos
                    };

                    item.PriceBreakdown = await BuildBreakdownAsync(itemDto, invoice.TaxRateId, ct);

                    invoice.Items.Add(item);
                    pos++;
                }
            }

            await _unitOfWork.SaveChangesAsync(ct);
            await RecalculateInvoiceTotalsAsync(id, ct);

            await InvalidateAfterInvoiceChangeAsync(id, ct);
            _log.LogInformation("Invoice updated. {InvoiceId}", id);
        }

        // =========================
        // DELETE INVOICE
        // =========================
        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            EnsureManualInvoicesEnabled();
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid invoice id.");

            var repo = _unitOfWork.Repo<Invoice>();

            var entity = await repo.Query(asNoTracking: false)
                .Include(x => x.Items)
                .Include(x => x.Totals)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (entity is null) return;

            repo.Remove(entity);
            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterInvoiceChangeAsync(id, ct);

            _log.LogInformation("Invoice deleted. {InvoiceId}", id);
        }

        // =========================
        // ITEMS: CREATE
        // =========================
        public async Task<Guid> CreateItemAsync(CreateInvoiceItemDto dto, CancellationToken ct = default)
        {
            EnsureManualInvoicesEnabled();
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.InvoiceId == Guid.Empty) throw new BadRequestAppException("Invalid invoice id.");

            var invoicesRepo = _unitOfWork.Repo<Invoice>();
            var itemsRepo = _unitOfWork.Repo<InvoiceItem>();

            var header = await invoicesRepo.Query(asNoTracking: true)
                .Where(x => x.Id == dto.InvoiceId)
                .Select(x => new { x.Id, x.TaxRateId })
                .FirstOrDefaultAsync(ct);

            if (header is null) throw new NotFoundAppException("Invoice not found.");

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var maxPos = await itemsRepo.Query(true)
                    .Where(x => x.InvoiceId == dto.InvoiceId)
                    .Select(x => (int?)x.Position)
                    .MaxAsync(ct) ?? 0;

                var breakdown = await BuildBreakdownAsync(dto.Item, header.TaxRateId, ct);

                var item = new InvoiceItem
                {
                    InvoiceId = dto.InvoiceId,
                    Title = (dto.Item.Title ?? "").Trim(),
                    ServiceId = dto.Item.ServiceId,
                    Quantity = dto.Item.Quantity,
                    UnitPrice = dto.Item.UnitPrice,
                    Config = dto.Item.Config ?? JsonDocument.Parse("{}"),
                    PriceBreakdown = breakdown,
                    TaxRateId = dto.Item.TaxRateId,
                    DiscountType = dto.Item.DiscountType,
                    DiscountValue = dto.Item.DiscountValue,
                    Position = dto.Item.Position > 0 ? dto.Item.Position : (maxPos + 1)
                };

                await itemsRepo.AddAsync(item, ct);
                await _unitOfWork.SaveChangesAsync(ct);

                await RecalculateInvoiceTotalsAsync(dto.InvoiceId, ct);

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterInvoiceChangeAsync(dto.InvoiceId, ct);
                _log.LogInformation("Invoice item created. {InvoiceId} {ItemId}", dto.InvoiceId, item.Id);

                return item.Id;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        // =========================
        // ITEMS: UPDATE
        // =========================
        public async Task UpdateItemAsync(UpdateInvoiceItemDto dto, CancellationToken ct = default)
        {
            EnsureManualInvoicesEnabled();
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.InvoiceId == Guid.Empty) throw new BadRequestAppException("Invalid invoice id.");
            if (dto.ItemId == Guid.Empty) throw new BadRequestAppException("Invalid item id.");

            var invoicesRepo = _unitOfWork.Repo<Invoice>();
            var itemsRepo = _unitOfWork.Repo<InvoiceItem>();

            var header = await invoicesRepo.Query(asNoTracking: true)
                .Where(x => x.Id == dto.InvoiceId)
                .Select(x => new { x.Id, x.TaxRateId })
                .FirstOrDefaultAsync(ct);

            if (header is null) throw new NotFoundAppException("Invoice not found.");

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var item = await itemsRepo.FirstOrDefaultAsync(
                    x => x.Id == dto.ItemId && x.InvoiceId == dto.InvoiceId,
                    ct,
                    asNoTracking: false);

                if (item is null) throw new NotFoundAppException("Invoice item not found.");

                item.Title = (dto.Item.Title ?? item.Title ?? "").Trim();
                item.ServiceId = dto.Item.ServiceId;
                item.Quantity = dto.Item.Quantity;
                item.UnitPrice = dto.Item.UnitPrice;
                item.Config = dto.Item.Config ?? item.Config ?? JsonDocument.Parse("{}");
                item.TaxRateId = dto.Item.TaxRateId;
                item.DiscountType = dto.Item.DiscountType;
                item.DiscountValue = dto.Item.DiscountValue;

                item.PriceBreakdown = await BuildBreakdownAsync(dto.Item, header.TaxRateId, ct);

                if (dto.Item.Position > 0)
                    item.Position = dto.Item.Position;

                itemsRepo.Update(item);

                await _unitOfWork.SaveChangesAsync(ct);
                await RecalculateInvoiceTotalsAsync(dto.InvoiceId, ct);

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterInvoiceChangeAsync(dto.InvoiceId, ct);
                _log.LogInformation("Invoice item updated. {InvoiceId} {ItemId}", dto.InvoiceId, dto.ItemId);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        // =========================
        // ITEMS: DELETE
        // =========================
        public async Task DeleteItemAsync(DeleteInvoiceItemDto dto, CancellationToken ct = default)
        {
            EnsureManualInvoicesEnabled();
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.InvoiceId == Guid.Empty) throw new BadRequestAppException("Invalid invoice id.");
            if (dto.ItemId == Guid.Empty) throw new BadRequestAppException("Invalid item id.");

            var itemsRepo = _unitOfWork.Repo<InvoiceItem>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var item = await itemsRepo.FirstOrDefaultAsync(
                    x => x.Id == dto.ItemId && x.InvoiceId == dto.InvoiceId,
                    ct,
                    asNoTracking: false);

                if (item is null) return;

                itemsRepo.Remove(item);
                await _unitOfWork.SaveChangesAsync(ct);

                await RepackPositionsAsync(dto.InvoiceId, ct);
                await RecalculateInvoiceTotalsAsync(dto.InvoiceId, ct);

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterInvoiceChangeAsync(dto.InvoiceId, ct);
                _log.LogInformation("Invoice item deleted. {InvoiceId} {ItemId}", dto.InvoiceId, dto.ItemId);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        // =========================
        // ITEMS: REORDER
        // =========================
        public async Task ReorderItemsAsync(ReorderInvoiceItemsDto dto, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.InvoiceId == Guid.Empty) throw new BadRequestAppException("Invalid invoice id.");
            if (dto.OrderedItemIds is null || dto.OrderedItemIds.Count == 0)
                throw new BadRequestAppException("OrderedItemIds is required.");

            var itemsRepo = _unitOfWork.Repo<InvoiceItem>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var items = await itemsRepo.ListAsync(x => x.InvoiceId == dto.InvoiceId, ct, asNoTracking: false);
                if (items.Count == 0) throw new NotFoundAppException("No items found.");

                var set = dto.OrderedItemIds.Distinct().ToList();

                var byId = items.ToDictionary(x => x.Id);
                foreach (var id in set)
                    if (!byId.ContainsKey(id))
                        throw new BadRequestAppException($"ItemId not found in invoice: {id}");

                int pos = 1;
                foreach (var id in set)
                {
                    var it = byId[id];
                    if (it.Position != pos)
                    {
                        it.Position = pos;
                        itemsRepo.Update(it);
                    }
                    pos++;
                }

                foreach (var it in items.Where(x => !set.Contains(x.Id)).OrderBy(x => x.Position))
                {
                    if (it.Position != pos)
                    {
                        it.Position = pos;
                        itemsRepo.Update(it);
                    }
                    pos++;
                }

                await _unitOfWork.SaveChangesAsync(ct);
                await RecalculateInvoiceTotalsAsync(dto.InvoiceId, ct);

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterInvoiceChangeAsync(dto.InvoiceId, ct);
                _log.LogInformation("Invoice items reordered. {InvoiceId}", dto.InvoiceId);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        // =========================
        // Price breakdown helper
        // =========================
        private async Task<JsonDocument> BuildBreakdownAsync(InvoiceItemDto itemDto, Guid? fallbackTaxRateId, CancellationToken ct)
        {
            var qty = itemDto.Quantity;
            var unit = itemDto.UnitPrice;

            var baseTotal = qty * unit;

            decimal discountAmount = 0m;
            if (itemDto.DiscountType is not null && itemDto.DiscountValue is not null)
            {
                var dv = itemDto.DiscountValue.Value;

                discountAmount = itemDto.DiscountType switch
                {
                    DiscountType.Fixed => Math.Min(baseTotal, dv),
                    DiscountType.Amount => Math.Min(baseTotal, dv),
                    DiscountType.Percent => Math.Min(baseTotal, baseTotal * (dv / 100m)),
                    _ => 0m
                };
            }

            var subTotal = Math.Max(0m, baseTotal - discountAmount);

            // tax
            var effectiveTaxRateId = itemDto.TaxRateId ?? fallbackTaxRateId;

            decimal taxRatePercent = 0m;
            decimal taxAmount = 0m;

            if (effectiveTaxRateId.HasValue)
            {
                var taxRepo = _unitOfWork.Repo<TaxRate>();
                var tax = await taxRepo.Query(asNoTracking: true)
                    .Where(t => t.Id == effectiveTaxRateId.Value && t.IsActive)
                    .Select(t => new { t.Id, t.Name, t.RatePercent })
                    .FirstOrDefaultAsync(ct);

                if (tax is not null)
                {
                    taxRatePercent = tax.RatePercent;
                    taxAmount = Math.Max(0m, subTotal * (taxRatePercent / 100m));
                }
            }

            var total = subTotal + taxAmount;

            var breakdownObj = new
            {
                qty,
                unitPrice = unit,
                baseTotal,
                discount = new
                {
                    type = itemDto.DiscountType?.ToString(),
                    value = itemDto.DiscountValue,
                    amount = discountAmount
                },
                tax = new
                {
                    taxRateId = effectiveTaxRateId,
                    ratePercent = taxRatePercent,
                    amount = taxAmount
                },
                subTotal,
                total
            };

            return JsonSerializer.SerializeToDocument(breakdownObj);
        }

        // =========================
        // Totals helper (InvoiceTotal)
        // =========================
        
private async Task RecalculateInvoiceTotalsAsync(Guid invoiceId, CancellationToken ct)
{
    var invoicesRepo = _unitOfWork.Repo<Invoice>();
    var taxRepo = _unitOfWork.Repo<TaxRate>();

    // Load tracked invoice with items/payments/totals so we can upsert InvoiceTotal without needing a separate repository
    var invoice = await invoicesRepo.Query(asNoTracking: false)
        .Include(x => x.Items)
        .Include(x => x.Payments)
        .Include(x => x.Totals)
        .FirstOrDefaultAsync(x => x.Id == invoiceId, ct);

    if (invoice is null) return;

    decimal subtotal = 0m;
    decimal itemDiscountTotal = 0m;
    decimal itemTaxTotal = 0m;

    foreach (var it in invoice.Items)
    {
        var (sub, disc, tax) = ReadBreakdown(it.PriceBreakdown, it.Quantity * it.UnitPrice);
        subtotal += sub;
        itemDiscountTotal += disc;
        itemTaxTotal += tax;
    }

    // Invoice-level discount (applied on subtotal)
    decimal invoiceDiscountAmount = 0m;
    if (invoice.InvoiceDiscountType is not null && invoice.InvoiceDiscountValue is not null)
    {
        var dv = invoice.InvoiceDiscountValue.Value;
        invoiceDiscountAmount = invoice.InvoiceDiscountType switch
        {
            DiscountType.Fixed => Math.Min(subtotal, dv),
            DiscountType.Amount => Math.Min(subtotal, dv),
            DiscountType.Percent => Math.Min(subtotal, subtotal * (dv / 100m)),
            _ => 0m
        };
    }

    var netSubtotal = Math.Max(0m, subtotal - invoiceDiscountAmount);

    // Invoice-level tax only when items tax is zero (avoid double-tax)
    decimal invoiceTaxAmount = 0m;
    if (invoice.TaxRateId.HasValue && itemTaxTotal == 0m)
    {
        var tax = await taxRepo.Query(asNoTracking: true)
            .Where(t => t.Id == invoice.TaxRateId.Value && t.IsActive)
            .Select(t => new { t.RatePercent })
            .FirstOrDefaultAsync(ct);

        if (tax is not null)
            invoiceTaxAmount = Math.Max(0m, netSubtotal * (tax.RatePercent / 100m));
    }

    var taxTotal = itemTaxTotal + invoiceTaxAmount;
    var discountTotal = itemDiscountTotal + invoiceDiscountAmount;
    var total = netSubtotal + taxTotal;

    var paidTotal = invoice.Payments.Sum(p => p.Amount);
    var balanceDue = Math.Max(0m, total - paidTotal);

    if (invoice.Totals is null)
    {
        invoice.Totals = new InvoiceTotal
        {
            InvoiceId = invoiceId
        };
    }

    invoice.Totals.Subtotal = netSubtotal;
    invoice.Totals.DiscountTotal = discountTotal;
    invoice.Totals.TaxTotal = taxTotal;
    invoice.Totals.Total = total;
    invoice.Totals.PaidTotal = paidTotal;
    invoice.Totals.BalanceDue = balanceDue;
    invoice.Totals.UpdatedAt = DateTimeOffset.UtcNow;

    await _unitOfWork.SaveChangesAsync(ct);
}

private static (decimal subTotal, decimal discountAmount, decimal taxAmount) ReadBreakdown(JsonDocument? doc, decimal fallbackBaseTotal)
        {
            try
            {
                if (doc is null) return (fallbackBaseTotal, 0m, 0m);

                var root = doc.RootElement;

                decimal sub = fallbackBaseTotal;
                decimal disc = 0m;
                decimal tax = 0m;

                if (root.TryGetProperty("subTotal", out var s) && s.TryGetDecimal(out var sd)) sub = sd;

                if (root.TryGetProperty("discount", out var d) && d.ValueKind == JsonValueKind.Object)
                {
                    if (d.TryGetProperty("amount", out var da) && da.TryGetDecimal(out var dd)) disc = dd;
                }

                if (root.TryGetProperty("tax", out var t) && t.ValueKind == JsonValueKind.Object)
                {
                    if (t.TryGetProperty("amount", out var ta) && ta.TryGetDecimal(out var td)) tax = td;
                }

                return (sub, disc, tax);
            }
            catch
            {
                return (fallbackBaseTotal, 0m, 0m);
            }
        }

        // =========================
        // Helpers
        // =========================
        private async Task InvalidateAfterInvoiceChangeAsync(Guid invoiceId, CancellationToken ct)
        {
            await _cache.RemoveAsync(InvoiceCacheKeys.Details(invoiceId), ct);
            await _cache.BumpVersionAsync(InvoiceCacheKeys.ListVersionKey, ct);
        }

        private async Task<string> GenerateInvoiceNoAsync(IRepository<Invoice> repo, CancellationToken ct)
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"I-{year}-";

            var last = await repo.Query(asNoTracking: true)
                .Where(x => x.InvoiceNo.StartsWith(prefix))
                .OrderByDescending(x => x.InvoiceNo)
                .Select(x => x.InvoiceNo)
                .FirstOrDefaultAsync(ct);

            var next = 1;
            if (!string.IsNullOrWhiteSpace(last))
            {
                var part = last.Substring(prefix.Length);
                if (int.TryParse(part, out var n)) next = n + 1;
            }

            return prefix + next.ToString("D6");
        }

        private static decimal CalcLineTotal(decimal qty, decimal unit, DiscountType? dt, decimal? dv)
        {
            var baseTotal = qty * unit;
            if (dt is null || dv is null) return baseTotal;

            return dt switch
            {
                DiscountType.Fixed => Math.Max(0, baseTotal - dv.Value),
                DiscountType.Amount => Math.Max(0, baseTotal - dv.Value),
                DiscountType.Percent => Math.Max(0, baseTotal - (baseTotal * (dv.Value / 100m))),
                _ => baseTotal
            };
        }

        private async Task RepackPositionsAsync(Guid invoiceId, CancellationToken ct)
        {
            var itemsRepo = _unitOfWork.Repo<InvoiceItem>();
            var items = await itemsRepo.Query(asNoTracking: false)
                .Where(x => x.InvoiceId == invoiceId)
                .OrderBy(x => x.Position)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync(ct);

            int pos = 1;
            foreach (var it in items)
            {
                if (it.Position != pos)
                {
                    it.Position = pos;
                    itemsRepo.Update(it);
                }
                pos++;
            }

            await _unitOfWork.SaveChangesAsync(ct);
        }

        private static string EscapeLike(string input)
            => input
                .Replace("!", "!!")
                .Replace("%", "!%")
                .Replace("_", "!_")
                .Replace("[", "![");
        // =========================
        // FEATURE GATE
        // =========================
        // For now we do NOT allow creating/editing invoices inside WitcherHub.
        // Invoices are generated/managed in Lexware and only displayed here.
        private const bool ManualInvoicesEnabled = false;

        private static void EnsureManualInvoicesEnabled()
        {
            if (!ManualInvoicesEnabled)
                throw new BadRequestAppException("Manual invoice management is disabled. Invoices are managed in Lexware.");
        }
    }

    }
