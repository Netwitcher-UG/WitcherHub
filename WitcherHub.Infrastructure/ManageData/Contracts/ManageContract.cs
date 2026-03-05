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
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.View.Contracts;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;
using NCalc;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WitcherHub.Application.Common.ConfigSchema;
using WitcherHub.Infrastructure.Data.Models;

namespace WitcherHub.Infrastructure.ManageData.Contracts
{
    public sealed class ManageContract : IContract
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppCache _cache;
        private readonly ILogger<ManageContract> _log;

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

        public ManageContract(IUnitOfWork unitOfWork, IAppCache cache, ILogger<ManageContract> log)
        {
            _unitOfWork = unitOfWork;
            _cache = cache;
            _log = log;
        }

        // =========================
        // LIST (By Project)
        // =========================
        public async Task<PagedResult<ContractViews.ContractListItemView>> GetContractsByProjectAsync(
            Guid projectId,
            int page = 1,
            int pageSize = 10,
            string? search = null,
            CancellationToken ct = default)
        {
            if (projectId == Guid.Empty) throw new BadRequestAppException("Invalid project id.");

            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 200 ? 10 : pageSize;

            var version = await _cache.GetOrCreateVersionAsync(ContractCacheKeys.ListVersionKey, ct);
            var cacheKey = ContractCacheKeys.ListByProjectWithVersion(page, pageSize, projectId, search, version);

            return await _cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    var repo = _unitOfWork.Repo<Contract>();
                    var q = repo.Query(asNoTracking: true).Where(x => x.ProjectId == projectId);

                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        var s = search.Trim();
                        var escaped = EscapeLike(s);
                        var pattern = $"%{escaped}%";

                        q = q.Where(x =>
                            EF.Functions.Like(x.ContractNo, pattern, "!") ||
                            (x.Terms != null && EF.Functions.Like(x.Terms, pattern, "!")) ||
                            x.Items.Any(i => EF.Functions.Like(i.Title, pattern, "!"))
                        );
                    }

                    var total = await q.LongCountAsync(token);
                    if (total == 0)
                        return PagedResult<ContractViews.ContractListItemView>.Empty(page, pageSize);

                    var items = await q
                        .OrderByDescending(x => x.CreatedAt)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .Select(x => new ContractViews.ContractListItemView
                        {
                            Id = x.Id,
                            ProjectId = x.ProjectId,
                            ContractNo = x.ContractNo,
                            Status = x.Status,
                            Currency = x.Currency,
                            CreatedAt = x.CreatedAt,
                            StartDate = x.StartDate,
                            EndDate = x.EndDate,
                            ItemsTotal = x.Items.Sum(i => (decimal?)(i.AgreedPrice ?? 0m)) ?? 0m
                        })
                        .ToListAsync(token);

                    return new PagedResult<ContractViews.ContractListItemView>
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
        public async Task<ContractViews.ContractDetailsView?> GetContractAsync(Guid id, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");

            var cacheKey = ContractCacheKeys.Details(id);

            return await _cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    var repo = _unitOfWork.Repo<Contract>();

                    var entity = await repo.Query(asNoTracking: true)
                        .Where(x => x.Id == id)
                        .Select(x => new ContractViews.ContractDetailsView
                        {
                            Id = x.Id,
                            ProjectId = x.ProjectId,

                            ContractNo = x.ContractNo,
                            Status = x.Status,
                            Currency = x.Currency,
                            InvoiceSendMode = x.InvoiceSendMode,
                            Terms = x.Terms,
                            TermsStructured = x.TermsStructured,
                            CreatedAt = x.CreatedAt,
                            StartDate = x.StartDate,
                            EndDate = x.EndDate,

                            SignedAt = x.SignedAt,

                            Items = x.Items
                                .OrderBy(i => i.Position)
                                .ThenBy(i => i.CreatedAt)
                                .Select(i => new ContractViews.ContractItemItemView
                                {
                                    Id = i.Id,
                                    ServiceId = i.ServiceId,
                                    ServiceName = i.Service != null ? i.Service.Name : null,
                                    Title = i.Title,
                                    Config = i.Config,

                                    Quantity = i.Quantity,
                                    UnitPrice = i.UnitPrice,
                                    BillingCycle = i.BillingCycle,

                                    DiscountType = i.DiscountType,
                                    DiscountValue = i.DiscountValue,

                                    PriceBreakdown = i.PriceBreakdown,
                                    AgreedPrice = i.AgreedPrice,
                                    Position = i.Position
                                })
                                .ToList(),

                            Signatures = x.Signatures
                                .OrderByDescending(s => s.CreatedAt)
                                .Select(s => new ContractViews.ContractSignatureView
                                {
                                    Id = s.Id,
                                    SignerName = s.SignerName,
                                    SignerEmail = s.SignerEmail,
                                    SignedAt = s.SignedAt,
                                    SignatureData = s.SignatureData
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
        // CREATE CONTRACT
        // =========================
        public async Task<Guid> CreateAsync(ContractDTOs dto, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.Contract.ProjectId == Guid.Empty) throw new BadRequestAppException("Invalid project id.");

            var projectsRepo = _unitOfWork.Repo<Project>();
            var contractsRepo = _unitOfWork.Repo<Contract>();

            var projectExists = await projectsRepo.AnyAsync(x => x.Id == dto.Contract.ProjectId, ct);
            if (!projectExists) throw new NotFoundAppException("Project not found.");

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var contractNo = await GenerateContractNoAsync(contractsRepo, ct);

                JsonDocument? structuredJson = null;

                if (dto.Contract.TermsStructured is ContractStructuredTermsDto structuredDto)
                {
                    var json = JsonSerializer.Serialize(structuredDto);
                    structuredJson = JsonDocument.Parse(json);
                }

                var contract = new Contract
                {
                    ProjectId = dto.Contract.ProjectId,
                    ContractNo = contractNo,
                    Status = dto.Contract.Status,
                    Currency = (dto.Contract.Currency ?? "EUR").Trim(),
                    Terms = string.IsNullOrWhiteSpace(dto.Contract.Terms) ? null : dto.Contract.Terms.Trim(),
                    TermsStructured = structuredJson,
                    StartDate = dto.Contract.StartDate,
                    EndDate = dto.Contract.EndDate,
                    SignedAt = dto.Contract.SignedAt
                };

                if (dto.Items is not null && dto.Items.Count > 0)
                {
                    int pos = 1;
                    foreach (var it in dto.Items.OrderBy(x => x.Position <= 0 ? int.MaxValue : x.Position))
                    {
                        contract.Items.Add(new ContractItem
                        {
                            Title = (it.Title ?? "").Trim(),
                            ServiceId = it.ServiceId,
                            Config = it.Config ?? JsonDocument.Parse("{}"),
                            AgreedPrice = it.AgreedPrice,
                            Position = it.Position > 0 ? it.Position : pos
                        });
                        pos++;
                    }
                }

                await contractsRepo.AddAsync(contract, ct);
                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterContractChangeAsync(contract.Id, ct);

                _log.LogInformation("Contract created. {ContractId} {ContractNo}", contract.Id, contract.ContractNo);
                return contract.Id;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }
        // =========================
        // UPDATE CONTRACT (header + optional replace items)
        // =========================
        public async Task UpdateAsync(Guid id, UpdateContractDto dto, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");
            if (dto is null) throw new BadRequestAppException("Invalid payload.");

            var contractsRepo = _unitOfWork.Repo<Contract>();

            var contract = await contractsRepo.Query(asNoTracking: false)
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (contract is null) throw new NotFoundAppException("Contract not found.");

            contract.Currency = (dto.Contract.Currency ?? contract.Currency ?? "EUR").Trim();
            contract.Terms = string.IsNullOrWhiteSpace(dto.Contract.Terms) ? null : dto.Contract.Terms.Trim();

            if (dto.Contract.TermsStructured is ContractStructuredTermsDto structuredDto)
            {
                var json = JsonSerializer.Serialize(structuredDto);
                contract.TermsStructured = JsonDocument.Parse(json);
            }

            contract.StartDate = dto.Contract.StartDate;
            contract.EndDate = dto.Contract.EndDate;
            contract.SignedAt = dto.Contract.SignedAt;
            contract.Status = dto.Contract.Status;

            if (dto.Items is not null)
            {
                contract.Items.Clear();

                int pos = 1;
                foreach (var it in dto.Items.OrderBy(x => x.Position <= 0 ? int.MaxValue : x.Position))
                {
                    contract.Items.Add(new ContractItem
                    {
                        ContractId = contract.Id,
                        Title = (it.Title ?? "").Trim(),
                        ServiceId = it.ServiceId,
                        Config = it.Config ?? JsonDocument.Parse("{}"),
                        AgreedPrice = it.AgreedPrice,
                        Position = it.Position > 0 ? it.Position : pos
                    });
                    pos++;
                }
            }

            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterContractChangeAsync(id, ct);
            _log.LogInformation("Contract updated. {ContractId}", id);
        }
        // =========================
        // DELETE CONTRACT
        // =========================
        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");

            var repo = _unitOfWork.Repo<Contract>();

            var entity = await repo.Query(asNoTracking: false)
                .Include(x => x.Items)
                .Include(x => x.Signatures)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (entity is null) return;

            repo.Remove(entity);
            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterContractChangeAsync(id, ct);

            _log.LogInformation("Contract deleted. {ContractId}", id);
        }

        public async Task UpdateHeaderAsync(
    Guid contractId,
    DocumentStatus status,
    DateOnly? startDate,
    DateOnly? endDate,
    string? terms,
    InvoiceSendMode invoiceSendMode,
    CancellationToken ct = default)
        {
            var repo = _unitOfWork.Repo<Contract>();

            var entity = await repo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(x => x.Id == contractId, ct);

            if (entity is null)
                throw new NotFoundAppException("Contract not found.");

            // ✅ لا تغيّر شي غير الهيدر
            entity.Status = status;
            entity.StartDate = startDate;
            entity.EndDate = endDate;
            entity.Terms = terms;

            entity.InvoiceSendMode = invoiceSendMode;

            repo.Update(entity);

            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterContractChangeAsync(contractId, ct);
        }

        // =========================
        // ITEMS: CREATE
        // =========================
        public async Task<Guid> CreateItemAsync(CreateContractItemDto dto, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.ContractId == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");

            var contractsRepo = _unitOfWork.Repo<Contract>();
            var itemsRepo = _unitOfWork.Repo<ContractItem>();

            var exists = await contractsRepo.AnyAsync(x => x.Id == dto.ContractId, ct);
            if (!exists) throw new NotFoundAppException("Contract not found.");

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var maxPos = await itemsRepo.Query(true)
                    .Where(x => x.ContractId == dto.ContractId)
                    .Select(x => (int?)x.Position)
                    .MaxAsync(ct) ?? 0;

                // ✅ Auto pricing ONLY when AgreedPrice is null (this keeps old manual flow intact)
                JsonDocument? breakdown = null;
                decimal? computedTotal = null;

                if (dto.Item.AgreedPrice is null)
                {
                    var (bd, total) = await BuildBreakdownAsync(dto.Item, ct);
                    breakdown = bd;
                    computedTotal = total;
                }

                var item = new ContractItem
                {
                    ContractId = dto.ContractId,
                    Title = (dto.Item.Title ?? "").Trim(),
                    ServiceId = dto.Item.ServiceId,
                    Config = dto.Item.Config ?? JsonDocument.Parse("{}"),

                    // ✅ keep manual if provided, otherwise auto
                    AgreedPrice = dto.Item.AgreedPrice ?? computedTotal,

                    Position = dto.Item.Position > 0 ? dto.Item.Position : (maxPos + 1),

                    // ✅ optional: إذا ضفت الأعمدة بالداتابيس (مستحسن ليتوافق تماماً مع Quotes)
                    Quantity = dto.Item.Quantity,
                    UnitPrice = dto.Item.UnitPrice,
                    BillingCycle = dto.Item.BillingCycle,
                    DiscountType = dto.Item.DiscountType,
                    DiscountValue = dto.Item.DiscountValue,
                    PriceBreakdown = breakdown
                };

                await itemsRepo.AddAsync(item, ct);
                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterContractChangeAsync(dto.ContractId, ct);
                _log.LogInformation("Contract item created. {ContractId} {ItemId}", dto.ContractId, item.Id);

                return item.Id;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        public async Task UpdateItemAsync(UpdateContractItemDto dto, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.ContractId == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");
            if (dto.ItemId == Guid.Empty) throw new BadRequestAppException("Invalid item id.");

            var itemsRepo = _unitOfWork.Repo<ContractItem>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var item = await itemsRepo.FirstOrDefaultAsync(
                    x => x.Id == dto.ItemId && x.ContractId == dto.ContractId,
                    ct,
                    asNoTracking: false);

                if (item is null) throw new NotFoundAppException("Contract item not found.");

                item.Title = (dto.Item.Title ?? item.Title ?? "").Trim();
                item.ServiceId = dto.Item.ServiceId;
                item.Config = dto.Item.Config ?? item.Config ?? JsonDocument.Parse("{}");

                // ✅ optional columns (if added)
                item.Quantity = dto.Item.Quantity;
                item.UnitPrice = dto.Item.UnitPrice;
                item.BillingCycle = dto.Item.BillingCycle;
                item.DiscountType = dto.Item.DiscountType;
                item.DiscountValue = dto.Item.DiscountValue;

                // ✅ Auto pricing ONLY when AgreedPrice is null
                if (dto.Item.AgreedPrice is null)
                {
                    var (bd,  total) = await BuildBreakdownAsync(dto.Item, ct);
                    item.PriceBreakdown = bd;
                    item.AgreedPrice = total;
                }
                else
                {
                    // old manual flow stays as-is
                    item.AgreedPrice = dto.Item.AgreedPrice;
                    item.PriceBreakdown = item.PriceBreakdown; // keep
                }

                if (dto.Item.Position > 0)
                    item.Position = dto.Item.Position;

                itemsRepo.Update(item);

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterContractChangeAsync(dto.ContractId, ct);
                _log.LogInformation("Contract item updated. {ContractId} {ItemId}", dto.ContractId, dto.ItemId);
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
        public async Task DeleteItemAsync(DeleteContractItemDto dto, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.ContractId == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");
            if (dto.ItemId == Guid.Empty) throw new BadRequestAppException("Invalid item id.");

            var itemsRepo = _unitOfWork.Repo<ContractItem>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var item = await itemsRepo.FirstOrDefaultAsync(
                    x => x.Id == dto.ItemId && x.ContractId == dto.ContractId,
                    ct,
                    asNoTracking: false);

                if (item is null) return;

                itemsRepo.Remove(item);
                await _unitOfWork.SaveChangesAsync(ct);

                await RepackPositionsAsync(dto.ContractId, ct);

                await _unitOfWork.CommitTransactionAsync(ct);

                await InvalidateAfterContractChangeAsync(dto.ContractId, ct);
                _log.LogInformation("Contract item deleted. {ContractId} {ItemId}", dto.ContractId, dto.ItemId);
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
        public async Task ReorderItemsAsync(ReorderContractItemsDto dto, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.ContractId == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");
            if (dto.OrderedItemIds is null || dto.OrderedItemIds.Count == 0)
                throw new BadRequestAppException("OrderedItemIds is required.");

            var itemsRepo = _unitOfWork.Repo<ContractItem>();

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var items = await itemsRepo.ListAsync(x => x.ContractId == dto.ContractId, ct, asNoTracking: false);
                if (items.Count == 0) throw new NotFoundAppException("No items found.");

                var set = dto.OrderedItemIds.Distinct().ToList();

                var byId = items.ToDictionary(x => x.Id);
                foreach (var id in set)
                    if (!byId.ContainsKey(id))
                        throw new BadRequestAppException($"ItemId not found in contract: {id}");

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

                await InvalidateAfterContractChangeAsync(dto.ContractId, ct);
                _log.LogInformation("Contract items reordered. {ContractId}", dto.ContractId);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                throw;
            }
        }

        // =========================
        // Helpers
        // =========================
        private async Task InvalidateAfterContractChangeAsync(Guid contractId, CancellationToken ct)
        {
            await _cache.RemoveAsync(ContractCacheKeys.Details(contractId), ct);
            await _cache.BumpVersionAsync(ContractCacheKeys.ListVersionKey, ct);
        }

        private async Task<string> GenerateContractNoAsync(IRepository<Contract> repo, CancellationToken ct)
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"C-{year}-";

            var last = await repo.Query(asNoTracking: true)
                .Where(x => x.ContractNo.StartsWith(prefix))
                .OrderByDescending(x => x.ContractNo)
                .Select(x => x.ContractNo)
                .FirstOrDefaultAsync(ct);

            var next = 1;
            if (!string.IsNullOrWhiteSpace(last))
            {
                var part = last.Substring(prefix.Length);
                if (int.TryParse(part, out var n)) next = n + 1;
            }

            return prefix + next.ToString("D6");
        }

        private async Task RepackPositionsAsync(Guid contractId, CancellationToken ct)
        {
            var itemsRepo = _unitOfWork.Repo<ContractItem>();
            var items = await itemsRepo.Query(asNoTracking: false)
                .Where(x => x.ContractId == contractId)
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
        private async Task<(JsonDocument Breakdown, decimal EffectiveUnitPrice)> BuildBreakdownAsync(ContractItemDto itemDto, CancellationToken ct)
        {
            var qty = itemDto.Quantity;

            // ✅ Trust base price from Service (not from UI)
            decimal baseUnit = itemDto.UnitPrice;
            JsonDocument? schemaDoc = null;
            PricingModel? pricingModel = null;

            if (itemDto.ServiceId.HasValue)
            {
                var svcRepo = _unitOfWork.Repo<ServiceCatalogItem>();
                var svc = await svcRepo.Query(asNoTracking: true)
                    .Where(s => s.Id == itemDto.ServiceId.Value)
                    .Select(s => new { s.BasePrice, s.ConfigSchema, s.PricingModel })
                    .FirstOrDefaultAsync(ct);

                if (svc is not null)
                {
                    baseUnit = svc.BasePrice;
                    schemaDoc = svc.ConfigSchema;
                    pricingModel = svc.PricingModel;
                }
            }

            // ✅ Apply defaults + validate config before rule evaluation
            if (schemaDoc is not null)
            {
                itemDto.Config ??= JsonDocument.Parse("{}");
                var normalized = ConfigSchemaLite.ApplyDefaults(schemaDoc, itemDto.Config);
                var errs = ConfigSchemaLite.Validate(schemaDoc, normalized);

                if (errs.Count > 0)
                {
                    var msg = string.Join("; ", errs.Take(5).Select(e => $"{e.Field}: {e.Message}"));
                    throw new BadRequestAppException("Invalid config for selected service. " + msg);
                }

                itemDto.Config = normalized;
            }

            var unit = baseUnit;
            var baseTotalBeforeRules = qty * baseUnit;
            var baseTotal = baseTotalBeforeRules;

            // ✅ discounts from rules are tracked separately
            decimal rulesDiscountAmount = 0m;
            var appliedRules = new List<object>();

            // ✅ Apply rules ONLY when PricingRuleIds is not empty (selected rules only)
            if (itemDto.ServiceId.HasValue && itemDto.PricingRuleIds is not null && itemDto.PricingRuleIds.Count > 0)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);

                var rulesRepo = _unitOfWork.Repo<PricingRule>();
                var rules = await rulesRepo.Query(asNoTracking: true)
                    .Where(r =>
                        r.ServiceId == itemDto.ServiceId.Value &&
                        r.IsActive &&
                        r.Scope == "LINE_ITEM" &&
                        itemDto.PricingRuleIds.Contains(r.Id))
                    .OrderBy(r => r.Priority)
                    .ThenBy(r => r.Name)
                    .ToListAsync(ct);

                var (vars, nameMap) = BuildVars(qty, baseUnit, unit, baseTotal, rulesDiscountAmount, itemDto.Config, pricingModel);

                foreach (var r in rules)
                {
                    if (r.ValidFrom.HasValue && today < r.ValidFrom.Value) continue;
                    if (r.ValidTo.HasValue && today > r.ValidTo.Value) continue;

                    var condExpr = NormalizeExpr(r.ConditionExpr, nameMap);

                    bool ok;
                    try { ok = EvalBool(condExpr, vars); }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "PricingRule condition eval failed. RuleId={RuleId} Expr={Expr}", r.Id, condExpr);
                        continue;
                    }
                    if (!ok) continue;

                    var valueExprNorm = NormalizeExpr(r.ValueExpr, nameMap);

                    decimal value;
                    try { value = EvalDecimal(valueExprNorm, vars); }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "PricingRule value eval failed. RuleId={RuleId} Expr={Expr}", r.Id, valueExprNorm);
                        continue;
                    }

                    var beforeTotal = baseTotal;
                    var beforeUnit = unit;
                    var beforeDiscount = rulesDiscountAmount;

                    string? interpretedAs = null;

                    switch (r.Action)
                    {
                        case RuleAction.Add:
                            baseTotal = baseTotal + value;
                            unit = qty != 0 ? baseTotal / qty : unit;
                            break;

                        case RuleAction.Multiply:
                            // Support % inputs if ValueExpr is numeric literal: 20 => +20% (x1.2)
                            if (IsNumericLiteral(r.ValueExpr) && value > 1m && value <= 100m)
                            {
                                interpretedAs = "Multiply(Percent)";
                                baseTotal = baseTotal * (1m + (value / 100m));
                            }
                            else
                            {
                                baseTotal = baseTotal * value;
                            }
                            unit = qty != 0 ? baseTotal / qty : unit;
                            break;

                        case RuleAction.SetUnit:
                            unit = value;
                            baseTotal = qty * unit;
                            break;

                        case RuleAction.SetTotal:
                            // Common UI case: value is numeric literal 1.2 meaning +20%
                            if (IsNumericLiteral(r.ValueExpr) && value > 0m && value < 10m)
                            {
                                interpretedAs = "SetTotal(Multiply)";
                                baseTotal = baseTotal * value;
                            }
                            else
                            {
                                baseTotal = value;
                            }
                            unit = qty != 0 ? baseTotal / qty : unit;
                            break;

                        case RuleAction.Discount:
                            {
                                var discountBase = Math.Max(0m, baseTotal - rulesDiscountAmount); // sequential discount base

                                decimal disc;
                                if (IsNumericLiteral(r.ValueExpr))
                                    disc = ComputeDiscountFromLiteral(value, discountBase);
                                else
                                    disc = Math.Min(discountBase, Math.Max(0m, value)); // computed => amount

                                if (disc > 0m)
                                    rulesDiscountAmount += disc;
                            }
                            break;

                        default:
                            break;
                    }

                    // Keep vars updated for subsequent rules
                    SetMoneyVar(vars, "unitPrice", unit);
                    SetMoneyVar(vars, "unit", unit);
                    SetMoneyVar(vars, "baseTotal", baseTotal);
                    SetMoneyVar(vars, "total", baseTotal);
                    SetMoneyVar(vars, "discountSoFar", rulesDiscountAmount);
                    SetMoneyVar(vars, "netTotal", Math.Max(0m, baseTotal - rulesDiscountAmount));

                    appliedRules.Add(new
                    {
                        id = r.Id,
                        name = r.Name,
                        action = r.Action.ToString(),
                        interpretedAs,
                        priority = r.Priority,
                        conditionExpr = r.ConditionExpr,
                        valueExpr = r.ValueExpr,
                        evaluatedValueExpr = valueExprNorm,
                        value,
                        beforeUnitPrice = beforeUnit,
                        afterUnitPrice = unit,
                        beforeTotal,
                        afterTotal = baseTotal,
                        discountApplied = Math.Max(0m, rulesDiscountAmount - beforeDiscount),
                        discountSoFar = rulesDiscountAmount
                    });
                }
            }

            // manual discount from item fields
            decimal fieldDiscountAmount = 0m;
            if (itemDto.DiscountType is not null && itemDto.DiscountValue is not null)
            {
                var dv = itemDto.DiscountValue.Value;
                fieldDiscountAmount = itemDto.DiscountType switch
                {
                    DiscountType.Fixed => Math.Min(baseTotal, dv),
                    DiscountType.Amount => Math.Min(baseTotal, dv),
                    DiscountType.Percent => Math.Min(baseTotal, baseTotal * (dv / 100m)),
                    _ => 0m
                };
            }

            rulesDiscountAmount = Math.Min(baseTotal, Math.Max(0m, rulesDiscountAmount));
            var totalDiscount = Math.Min(baseTotal, Math.Max(0m, fieldDiscountAmount + rulesDiscountAmount));
            var subTotal = Math.Max(0m, baseTotal - totalDiscount);

            // =========================
            // TAX (DISABLED PER LINE)
            // VAT will be applied on the whole quote (header) only
            // =========================
            decimal taxRatePercent = 0m;
            decimal taxAmount = 0m;

            var total = subTotal; // ✅ لا ضريبة على السطر

            var breakdownObj = new
            {
                qty,
                pricingModel = pricingModel?.ToString(),
                baseUnitPrice = baseUnit,
                unitPrice = unit,
                baseTotalBeforeRules,
                pricingRules = appliedRules,
                baseTotal,
                discount = new
                {
                    type = itemDto.DiscountType?.ToString(),
                    value = itemDto.DiscountValue,
                    fromField = fieldDiscountAmount,
                    fromRules = rulesDiscountAmount,
                    amount = totalDiscount
                },
                tax = new
                {
                    applyTax = false,
                    ratePercent = 0m,
                    amount = 0m
                },
                subTotal,
                total
            };

            return (JsonSerializer.SerializeToDocument(breakdownObj), unit);
        }

        private static (Dictionary<string, object?> Vars, Dictionary<string, string> NameMap) BuildVars(
            decimal qty,
            decimal baseUnit,
            decimal unit,
            decimal baseTotal,
            decimal discountSoFar,
            JsonDocument? config,
            PricingModel? pricingModel)
        {
            var vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["qty"] = (double)qty,
                ["quantity"] = (double)qty,

                ["baseUnitPrice"] = (double)baseUnit,
                ["basePrice"] = (double)baseUnit,
                ["baseUnit"] = (double)baseUnit,

                ["unitPrice"] = (double)unit,
                ["unit"] = (double)unit,

                ["baseTotal"] = (double)baseTotal,
                ["total"] = (double)baseTotal,

                ["discountSoFar"] = (double)discountSoFar,
                ["netTotal"] = (double)Math.Max(0m, baseTotal - discountSoFar),

                ["pricingModel"] = pricingModel?.ToString()
            };

            var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["qty"] = "qty",
                ["quantity"] = "qty",
                ["baseUnitPrice"] = "baseUnitPrice",
                ["basePrice"] = "basePrice",
                ["unitPrice"] = "unitPrice",
                ["unit"] = "unit",
                ["baseTotal"] = "baseTotal",
                ["total"] = "total",
                ["discountSoFar"] = "discountSoFar",
                ["netTotal"] = "netTotal",
                ["pricingModel"] = "pricingModel"
            };

            if (config is not null && config.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in config.RootElement.EnumerateObject())
                {
                    object? val = p.Value.ValueKind switch
                    {
                        JsonValueKind.Number => p.Value.TryGetDouble(out var d) ? d : 0d,
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.String => p.Value.GetString(),
                        _ => null
                    };

                    var safe = ToSafeVarName(p.Name);

                    if (vars.ContainsKey(safe))
                    {
                        if (!(nameMap.TryGetValue(p.Name, out var existing) && string.Equals(existing, safe, StringComparison.OrdinalIgnoreCase)))
                        {
                            var i = 2;
                            var candidate = $"{safe}_{i}";
                            while (vars.ContainsKey(candidate)) { i++; candidate = $"{safe}_{i}"; }
                            safe = candidate;
                        }
                    }

                    nameMap[p.Name] = safe;
                    nameMap[safe] = safe;
                    vars[safe] = val;
                }

                // derived convenience var
                if (TryGetDouble(vars, "durationMinutes", out var mins) && !vars.ContainsKey("hours"))
                {
                    vars["hours"] = mins / 60d;
                    nameMap["hours"] = "hours";
                }
            }

            return (vars, nameMap);
        }

        private static bool TryGetDouble(Dictionary<string, object?> vars, string key, out double value)
        {
            value = 0d;
            if (!vars.TryGetValue(key, out var v) || v is null) return false;

            try
            {
                value = v switch
                {
                    double d => d,
                    float f => f,
                    int i => i,
                    long l => l,
                    decimal m => (double)m,
                    _ => Convert.ToDouble(v, CultureInfo.InvariantCulture)
                };
                return true;
            }
            catch { return false; }
        }

        private static void SetMoneyVar(Dictionary<string, object?> vars, string key, decimal value)
            => vars[key] = (double)value;

        private static string NormalizeExpr(string expr, Dictionary<string, string> nameMap)
        {
            if (string.IsNullOrWhiteSpace(expr)) return expr;

            var s = expr.Replace("\\\"", "\"").Trim();

            // params["x"] / params['x']  ==> mapped var name
            s = Regex.Replace(
                s,
                @"\bparams\s*\[\s*([""'])(?<k>[^""']+)\1\s*\]",
                m =>
                {
                    var key = m.Groups["k"].Value;
                    if (nameMap.TryGetValue(key, out var mapped)) return mapped;
                    return ToSafeVarName(key);
                },
                RegexOptions.IgnoreCase);

            // params.x ==> mapped
            s = Regex.Replace(
                s,
                @"\bparams\.(?<k>[A-Za-z_][A-Za-z0-9_]*)\b",
                m =>
                {
                    var key = m.Groups["k"].Value;
                    return nameMap.TryGetValue(key, out var mapped) ? mapped : key;
                },
                RegexOptions.IgnoreCase);

            return s;
        }

        private static string ToSafeVarName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "p";

            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
                else sb.Append('_');
            }

            var s = sb.ToString();
            if (char.IsDigit(s[0])) s = "_" + s;
            return s;
        }

        private static bool IsNumericLiteral(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return false;
            var s = expr.Trim();
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
        }

        private static decimal ComputeDiscountFromLiteral(decimal value, decimal baseAmount)
        {
            if (baseAmount <= 0m) return 0m;
            if (value <= 0m) return 0m;

            // 0.10 => 10% ; 10 => 10% ; 250 => amount
            if (value <= 1m)
                return Math.Min(baseAmount, baseAmount * value);

            if (value <= 100m)
                return Math.Min(baseAmount, baseAmount * (value / 100m));

            return Math.Min(baseAmount, value);
        }

        private static bool EvalBool(string expr, Dictionary<string, object?> vars)
        {
            var e = new Expression(expr);
            // default missing vars to 0 instead of throwing
            e.EvaluateParameter += (_, args) => { args.Result = 0d; };

            foreach (var kv in vars) e.Parameters[kv.Key] = kv.Value;

            var r = e.Evaluate();
            return r is bool b ? b : Convert.ToBoolean(r, CultureInfo.InvariantCulture);
        }

        private static decimal EvalDecimal(string expr, Dictionary<string, object?> vars)
        {
            var e = new Expression(expr);

            e.EvaluateParameter += (_, args) => { args.Result = 0d; };

            foreach (var kv in vars) e.Parameters[kv.Key] = kv.Value;

            var r = e.Evaluate();

            return r switch
            {
                decimal m => m,
                double d => (decimal)d,
                float f => (decimal)f,
                int i => i,
                long l => l,
                _ => Convert.ToDecimal(r, CultureInfo.InvariantCulture)
            };
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

  

        private static string EscapeLike(string input)
            => input
                .Replace("!", "!!")
                .Replace("%", "!%")
                .Replace("_", "!_")
                .Replace("[", "![");
    }


}
