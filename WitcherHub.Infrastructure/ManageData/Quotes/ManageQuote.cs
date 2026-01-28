using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Common.CacheKeys;
using WitcherHub.Application.Common.Caching;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Quotes;
using WitcherHub.Application.Models.View.Quotes;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.ManageData.Quotes
{
    public sealed class ManageQuote : IQuote
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppCache _cache;
        private readonly ILogger<ManageQuote> _log;

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

        public ManageQuote(IUnitOfWork unitOfWork, IAppCache cache, ILogger<ManageQuote> log)
        {
            _unitOfWork = unitOfWork;
            _cache = cache;
            _log = log;
        }

        // =========================
        // LIST (By Project) + Pagination + Search
        // =========================
        public async Task<PagedResult<QuoteViews.QuoteListItemView>> GetQuotesByProjectAsync(
            Guid projectId,
            int page = 1,
            int pageSize = 10,
            string? search = null,
            CancellationToken ct = default)
        {
            if (projectId == Guid.Empty) throw new BadRequestAppException("Invalid project id.");

            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 200 ? 10 : pageSize;

            var version = await _cache.GetOrCreateVersionAsync(QuoteCacheKeys.ListVersionKey, ct);
            var cacheKey = QuoteCacheKeys.ListByProjectWithVersion(page, pageSize, projectId, search, version);

            return await _cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    var repo = _unitOfWork.Repo<Quote>();
                    var q = repo.Query(asNoTracking: true).Where(x => x.ProjectId == projectId);

                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        var s = search.Trim();
                        var escaped = EscapeLike(s);
                        var pattern = $"%{escaped}%";

                        q = q.Where(x =>
                            EF.Functions.Like(x.QuoteNo, pattern, "!") ||
                            (x.Notes != null && EF.Functions.Like(x.Notes, pattern, "!")) ||
                            x.Items.Any(i => EF.Functions.Like(i.Title, pattern, "!"))
                        );
                    }

                    var total = await q.LongCountAsync(token);
                    if (total == 0)
                        return PagedResult<QuoteViews.QuoteListItemView>.Empty(page, pageSize);

                    var items = await q
                        .OrderByDescending(x => x.CreatedAt)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .Select(x => new QuoteViews.QuoteListItemView
                        {
                            Id = x.Id,
                            ProjectId = x.ProjectId,
                            QuoteNo = x.QuoteNo,
                            Status = x.Status,
                            Currency = x.Currency,
                            CreatedAt = x.CreatedAt,
                            IssuedAt = x.IssuedAt,
                            ExpiresAt = x.ExpiresAt,

                            ItemsTotal = x.Items.Sum(i => i.Quantity * i.UnitPrice) // discount/tax later
                        })
                        .ToListAsync(token);

                    return new PagedResult<QuoteViews.QuoteListItemView>
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
        public async Task<QuoteViews.QuoteDetailsView?> GetQuoteAsync(Guid id, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid quote id.");

            var cacheKey = QuoteCacheKeys.Details(id);

            return await _cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    var repo = _unitOfWork.Repo<Quote>();

                    var entity = await repo.Query(asNoTracking: true)
                        .Where(x => x.Id == id)
                        .Select(x => new QuoteViews.QuoteDetailsView
                        {
                            Id = x.Id,
                            ProjectId = x.ProjectId,
                            QuoteNo = x.QuoteNo,
                            Status = x.Status,
                            Currency = x.Currency,
                            Notes = x.Notes,
                            CreatedAt = x.CreatedAt,
                            IssuedAt = x.IssuedAt,
                            ExpiresAt = x.ExpiresAt,

                            Items = x.Items
                                .OrderBy(i => i.Position)
                                .ThenBy(i => i.CreatedAt)
                                .Select(i => new QuoteViews.QuoteItemItemView
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
        // CREATE QUOTE (with optional items)
        // =========================
        public async Task<Guid> CreateAsync(QuoteDTOs dto, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.Quote.ProjectId == Guid.Empty) throw new BadRequestAppException("Invalid project id.");

            var projectsRepo = _unitOfWork.Repo<Project>();
            var quotesRepo = _unitOfWork.Repo<Quote>();

            var projectExists = await projectsRepo.AnyAsync(x => x.Id == dto.Quote.ProjectId, ct);
            if (!projectExists) throw new NotFoundAppException("Project not found.");

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var quoteNo = await GenerateQuoteNoAsync(quotesRepo, ct);

                var quote = new Quote
                {
                    ProjectId = dto.Quote.ProjectId,
                    QuoteNo = quoteNo,

                    Status = dto.Quote.Status,
                    Currency = (dto.Quote.Currency ?? "EUR").Trim(),
                    Notes = string.IsNullOrWhiteSpace(dto.Quote.Notes) ? null : dto.Quote.Notes.Trim(),
                    IssuedAt = dto.Quote.IssuedAt,
                    ExpiresAt = dto.Quote.ExpiresAt
                };

                // Items (optional)
                if (dto.Items is not null && dto.Items.Count > 0)
                {
                    int pos = 1;
                    foreach (var it in dto.Items.OrderBy(x => x.Position <= 0 ? int.MaxValue : x.Position))
                    {
                        var item = new QuoteItem
                        {
                            Title = (it.Title ?? "").Trim(),
                            ServiceId = it.ServiceId,
                            Quantity = it.Quantity,
                            UnitPrice = it.UnitPrice,
                            Config = it.Config ?? JsonDocument.Parse("{}"),
                            PriceBreakdown = it.PriceBreakdown,
                            TaxRateId = it.TaxRateId,
                            DiscountType = it.DiscountType,
                            DiscountValue = it.DiscountValue,
                            Position = it.Position > 0 ? it.Position : pos
                        };
                        quote.Items.Add(item);
                        pos++;
                    }
                }

                await quotesRepo.AddAsync(quote, ct);
                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterQuoteChangeAsync(quote.Id, ct);

                _log.LogInformation("Quote created. {QuoteId} {QuoteNo}", quote.Id, quote.QuoteNo);
                return quote.Id;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        // =========================
        // UPDATE QUOTE (header only + optional replace items)
        // =========================
        public async Task UpdateAsync(Guid id, UpdateQuoteDto dto, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid quote id.");
            if (dto is null) throw new BadRequestAppException("Invalid payload.");

            var quotesRepo = _unitOfWork.Repo<Quote>();

            var quote = await quotesRepo.Query(asNoTracking: false)
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (quote is null) throw new NotFoundAppException("Quote not found.");

            quote.Currency = (dto.Quote.Currency ?? quote.Currency ?? "EUR").Trim();
            quote.Notes = string.IsNullOrWhiteSpace(dto.Quote.Notes) ? null : dto.Quote.Notes.Trim();
            quote.IssuedAt = dto.Quote.IssuedAt;
            quote.ExpiresAt = dto.Quote.ExpiresAt;

            EnsureValidQuoteStatus(dto.Quote.Status);
            quote.Status = dto.Quote.Status;

            if (dto.Items is not null)
            {
                quote.Items.Clear();

                int pos = 1;
                foreach (var it in dto.Items.OrderBy(x => x.Position <= 0 ? int.MaxValue : x.Position))
                {
                    quote.Items.Add(new QuoteItem
                    {
                        QuoteId = quote.Id,
                        Title = (it.Title ?? "").Trim(),
                        ServiceId = it.ServiceId,
                        Quantity = it.Quantity,
                        UnitPrice = it.UnitPrice,
                        Config = it.Config ?? JsonDocument.Parse("{}"),
                        PriceBreakdown = it.PriceBreakdown,
                        TaxRateId = it.TaxRateId,
                        DiscountType = it.DiscountType,
                        DiscountValue = it.DiscountValue,
                        Position = it.Position > 0 ? it.Position : pos
                    });
                    pos++;
                }
            }

            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterQuoteChangeAsync(id, ct);
            _log.LogInformation("Quote updated. {QuoteId}", id);
        }

        // =========================
        // DELETE QUOTE
        // =========================
        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid quote id.");

            var repo = _unitOfWork.Repo<Quote>();

            var entity = await repo.Query(asNoTracking: false)
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (entity is null) return;

            repo.Remove(entity);
            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterQuoteChangeAsync(id, ct);

            _log.LogInformation("Quote deleted. {QuoteId}", id);
        }

        // =========================
        // ITEMS: CREATE
        // =========================
        public async Task<Guid> CreateItemAsync(CreateQuoteItemDto dto, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.QuoteId == Guid.Empty) throw new BadRequestAppException("Invalid quote id.");

            var quotesRepo = _unitOfWork.Repo<Quote>();
            var itemsRepo = _unitOfWork.Repo<QuoteItem>();

            var exists = await quotesRepo.AnyAsync(x => x.Id == dto.QuoteId, ct);
            if (!exists) throw new NotFoundAppException("Quote not found.");

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var maxPos = await itemsRepo.Query(true)
                    .Where(x => x.QuoteId == dto.QuoteId)
                    .Select(x => (int?)x.Position)
                    .MaxAsync(ct) ?? 0;

                var breakdown = await BuildBreakdownAsync(dto.Item, ct);

                var item = new QuoteItem
                {
                    QuoteId = dto.QuoteId,
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
                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterQuoteChangeAsync(dto.QuoteId, ct);
                _log.LogInformation("Quote item created. {QuoteId} {ItemId}", dto.QuoteId, item.Id);

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
        public async Task UpdateItemAsync(UpdateQuoteItemDto dto, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.QuoteId == Guid.Empty) throw new BadRequestAppException("Invalid quote id.");
            if (dto.ItemId == Guid.Empty) throw new BadRequestAppException("Invalid item id.");

            var itemsRepo = _unitOfWork.Repo<QuoteItem>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var item = await itemsRepo.FirstOrDefaultAsync(
                    x => x.Id == dto.ItemId && x.QuoteId == dto.QuoteId,
                    ct,
                    asNoTracking: false);

                if (item is null) throw new NotFoundAppException("Quote item not found.");

                item.Title = (dto.Item.Title ?? item.Title ?? "").Trim();
                item.ServiceId = dto.Item.ServiceId;
                item.Quantity = dto.Item.Quantity;
                item.UnitPrice = dto.Item.UnitPrice;
                item.Config = dto.Item.Config ?? item.Config ?? JsonDocument.Parse("{}");
                item.TaxRateId = dto.Item.TaxRateId;
                item.DiscountType = dto.Item.DiscountType;
                item.DiscountValue = dto.Item.DiscountValue;

                // Recalculate breakdown every update
                item.PriceBreakdown = await BuildBreakdownAsync(dto.Item, ct);

                if (dto.Item.Position > 0)
                    item.Position = dto.Item.Position;

                itemsRepo.Update(item);

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterQuoteChangeAsync(dto.QuoteId, ct);
                _log.LogInformation("Quote item updated. {QuoteId} {ItemId}", dto.QuoteId, dto.ItemId);
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
        public async Task DeleteItemAsync(DeleteQuoteItemDto dto, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.QuoteId == Guid.Empty) throw new BadRequestAppException("Invalid quote id.");
            if (dto.ItemId == Guid.Empty) throw new BadRequestAppException("Invalid item id.");

            var itemsRepo = _unitOfWork.Repo<QuoteItem>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var item = await itemsRepo.FirstOrDefaultAsync(
                    x => x.Id == dto.ItemId && x.QuoteId == dto.QuoteId,
                    ct,
                    asNoTracking: false);

                if (item is null) return;

                itemsRepo.Remove(item);
                await _unitOfWork.SaveChangesAsync(ct);

                await RepackPositionsAsync(dto.QuoteId, ct);

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterQuoteChangeAsync(dto.QuoteId, ct);
                _log.LogInformation("Quote item deleted. {QuoteId} {ItemId}", dto.QuoteId, dto.ItemId);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        // =========================
        // ITEMS: REORDER (by explicit ordered ids)
        // =========================
        public async Task ReorderItemsAsync(ReorderQuoteItemsDto dto, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.QuoteId == Guid.Empty) throw new BadRequestAppException("Invalid quote id.");
            if (dto.OrderedItemIds is null || dto.OrderedItemIds.Count == 0)
                throw new BadRequestAppException("OrderedItemIds is required.");

            var itemsRepo = _unitOfWork.Repo<QuoteItem>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var items = await itemsRepo.ListAsync(x => x.QuoteId == dto.QuoteId, ct, asNoTracking: false);
                if (items.Count == 0) throw new NotFoundAppException("No items found.");

                var set = dto.OrderedItemIds.Distinct().ToList();

                var byId = items.ToDictionary(x => x.Id);
                foreach (var id in set)
                    if (!byId.ContainsKey(id))
                        throw new BadRequestAppException($"ItemId not found in quote: {id}");

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

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterQuoteChangeAsync(dto.QuoteId, ct);
                _log.LogInformation("Quote items reordered. {QuoteId}", dto.QuoteId);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        // =========================
        // Price breakdown helper (strongly typed)
        // =========================
        private async Task<JsonDocument> BuildBreakdownAsync(QuoteItemDto itemDto, CancellationToken ct)
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

            decimal taxRatePercent = 0m;
            decimal taxAmount = 0m;

            if (itemDto.TaxRateId.HasValue)
            {
                var taxRepo = _unitOfWork.Repo<TaxRate>();
                var tax = await taxRepo.Query(asNoTracking: true)
                    .Where(t => t.Id == itemDto.TaxRateId.Value && t.IsActive)
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
                    taxRateId = itemDto.TaxRateId,
                    ratePercent = taxRatePercent,
                    amount = taxAmount
                },
                subTotal,
                total
            };

            return JsonSerializer.SerializeToDocument(breakdownObj);
        }

        // =========================
        // Helpers
        // =========================
        private async Task InvalidateAfterQuoteChangeAsync(Guid quoteId, CancellationToken ct)
        {
            await _cache.RemoveAsync(QuoteCacheKeys.Details(quoteId), ct);
            await _cache.BumpVersionAsync(QuoteCacheKeys.ListVersionKey, ct);
        }

        private static void EnsureValidQuoteStatus(DocumentStatus status)
        {
            if (status is DocumentStatus.Paid or DocumentStatus.Void)
                throw new BadRequestAppException("Invalid status for quote.");
        }

        private async Task<string> GenerateQuoteNoAsync(IRepository<Quote> repo, CancellationToken ct)
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"Q-{year}-";

            var last = await repo.Query(asNoTracking: true)
                .Where(x => x.QuoteNo.StartsWith(prefix))
                .OrderByDescending(x => x.QuoteNo)
                .Select(x => x.QuoteNo)
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

        private async Task RepackPositionsAsync(Guid quoteId, CancellationToken ct)
        {
            var itemsRepo = _unitOfWork.Repo<QuoteItem>();
            var items = await itemsRepo.Query(asNoTracking: false)
                .Where(x => x.QuoteId == quoteId)
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
    }
}
