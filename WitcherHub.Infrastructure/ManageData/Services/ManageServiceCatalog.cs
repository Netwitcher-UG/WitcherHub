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
using WitcherHub.Application.Models.DTO.Services;
using WitcherHub.Application.Models.View.Services;
using WitcherHub.Infrastructure.Data.Models;

namespace WitcherHub.Infrastructure.ManageData.Services
{
    public sealed class ManageServiceCatalog : IServiceCatalog
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppCache _cache;
        private readonly ILogger<ManageServiceCatalog> _log;

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

        public ManageServiceCatalog(IUnitOfWork unitOfWork, IAppCache cache, ILogger<ManageServiceCatalog> log)
        {
            _unitOfWork = unitOfWork;
            _cache = cache;
            _log = log;
        }

        // =========================
        // Listing (Pagination + Search)
        // =========================
        public async Task<PagedResult<ServiceViews.ServiceListItemView>> GetServicesAsync(
            int page = 1,
            int pageSize = 10,
            string? search = null,
            CancellationToken ct = default)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 200 ? 10 : pageSize;

            var version = await _cache.GetOrCreateVersionAsync(ServiceCacheKeys.ListVersionKey, ct);
            var cacheKey = ServiceCacheKeys.ListWithVersion(page, pageSize, search, version);

            return await _cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    var repo = _unitOfWork.Repo<ServiceCatalogItem>();
                    var q = repo.Query(asNoTracking: true);

                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        var s = search.Trim();
                        var escaped = EscapeLike(s);
                        var pattern = $"%{escaped}%";

                        q = q.Where(x =>
                            EF.Functions.Like(x.Name, pattern, "!") ||
                            EF.Functions.Like(x.DefaultCurrency, pattern, "!"));
                    }

                    var total = await q.LongCountAsync(token);
                    if (total == 0)
                        return PagedResult<ServiceViews.ServiceListItemView>.Empty(page, pageSize);

                    // مهم: RulesCount على DB
                    var items = await q
                        .OrderBy(x => x.Name)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .Select(x => new ServiceViews.ServiceListItemView
                        {
                            Id = x.Id,
                            Name = x.Name,
                            ServiceType = x.ServiceType,
                            PricingModel = x.PricingModel,
                            BasePrice = x.BasePrice,
                            DefaultCurrency = x.DefaultCurrency,
                            IsActive = x.IsActive,
                            RulesCount = x.PricingRules.Count
                        }).ToListAsync(token);

                    return new PagedResult<ServiceViews.ServiceListItemView>
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
        public async Task<ServiceViews.ServiceDetailsView?> GetServiceAsync(Guid id, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid service id.");

            var cacheKey = ServiceCacheKeys.Details(id);

            return await _cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    var repo = _unitOfWork.Repo<ServiceCatalogItem>();

                    var entity = await repo.GetByIdAsync(
                        id,
                        ct: token,
                        asNoTracking: true,
                        x => x.PricingRules);

                    if (entity is null) return null;

                    return new ServiceViews.ServiceDetailsView
                    {
                        Id = entity.Id,
                        Name = entity.Name,
                        ServiceType = entity.ServiceType,
                        PricingModel = entity.PricingModel,
                        BasePrice = entity.BasePrice,
                        DefaultCurrency = entity.DefaultCurrency,
                        IsActive = entity.IsActive,
                        ConfigSchema = entity.ConfigSchema,

                        PricingRules = entity.PricingRules
                            .OrderBy(r => r.Priority)
                            .ThenBy(r => r.Name)
                            .Select(r => new ServiceViews.PricingRuleItemView
                            {
                                Id = r.Id,
                                Name = r.Name,
                                Priority = r.Priority,
                                ConditionExpr = r.ConditionExpr,
                                Action = r.Action,
                                ValueExpr = r.ValueExpr,
                                Label = r.Label,
                                Scope = r.Scope,
                                IsActive = r.IsActive,
                                ValidFrom = r.ValidFrom,
                                ValidTo = r.ValidTo
                            })
                            .ToList()
                    };
                },
                DetailsCacheOptions,
                ct);
        }

        // =========================
        // Service CRUD
        // =========================
        public async Task<Guid> CreateAsync(ServiceCatalogDTOs dto, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");

            var servicesRepo = _unitOfWork.Repo<ServiceCatalogItem>();

            var service = new ServiceCatalogItem
            {
                Name = (dto.Service.Name ?? "").Trim(),
                ServiceType = dto.Service.ServiceType,
                PricingModel = dto.Service.PricingModel,
                BasePrice = dto.Service.BasePrice,
                DefaultCurrency = (dto.Service.DefaultCurrency ?? "EUR").Trim(),
                IsActive = dto.Service.IsActive,
                ConfigSchema = ParseJsonOrNull(dto.Service.ConfigSchemaJson)
            };

            // (اختياري) إضافة rules أثناء الإنشاء
            if (dto.PricingRules is not null && dto.PricingRules.Count > 0)
            {
                foreach (var r in dto.PricingRules)
                {
                    service.PricingRules.Add(new PricingRule
                    {
                        Name = (r.Name ?? "").Trim(),
                        Priority = r.Priority,
                        ConditionExpr = string.IsNullOrWhiteSpace(r.ConditionExpr) ? "true" : r.ConditionExpr.Trim(),
                        Action = r.Action,
                        ValueExpr = string.IsNullOrWhiteSpace(r.ValueExpr) ? "0" : r.ValueExpr.Trim(),
                        Label = string.IsNullOrWhiteSpace(r.Label) ? null : r.Label.Trim(),
                        Scope = string.IsNullOrWhiteSpace(r.Scope) ? "LINE_ITEM" : r.Scope.Trim(),
                        IsActive = r.IsActive,
                        ValidFrom = r.ValidFrom,
                        ValidTo = r.ValidTo
                    });
                }
            }

            await servicesRepo.AddAsync(service, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterServiceChangeAsync(service.Id, ct);

            _log.LogInformation("Service created. {ServiceId}", service.Id);
            return service.Id;
        }

        public async Task UpdateAsync(Guid id, UpdateServiceCatalogItemDto dto, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid service id.");
            if (dto is null) throw new BadRequestAppException("Invalid payload.");

            var servicesRepo = _unitOfWork.Repo<ServiceCatalogItem>();

            var service = await servicesRepo.GetByIdAsync(id, ct: ct, asNoTracking: false, x => x.PricingRules);
            if (service is null) throw new NotFoundAppException("Service not found.");

            var s = dto.Service ?? throw new BadRequestAppException("Missing service data.");

            service.Name = (s.Name ?? "").Trim();
            service.ServiceType = s.ServiceType;
            service.PricingModel = s.PricingModel;
            service.BasePrice = s.BasePrice;
            service.DefaultCurrency = (s.DefaultCurrency ?? "EUR").Trim();
            service.IsActive = s.IsActive;

            // ConfigSchemaJson behavior:
            // null => لا تغيّر
            // ""   => clear
            // else => replace
            if (s.ConfigSchemaJson is not null)
            {
                service.ConfigSchema = string.IsNullOrWhiteSpace(s.ConfigSchemaJson)
                    ? null
                    : ParseJsonOrNull(s.ConfigSchemaJson);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            await InvalidateAfterServiceChangeAsync(id, ct);

            _log.LogInformation("Service updated. {ServiceId}", id);
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid service id.");

            var repo = _unitOfWork.Repo<ServiceCatalogItem>();

            var entity = await repo.GetByIdAsync(id, ct: ct, asNoTracking: false, x => x.PricingRules);
            if (entity is null) return;

            repo.Remove(entity);
            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterServiceChangeAsync(id, ct);

            _log.LogInformation("Service deleted. {ServiceId}", id);
        }

        // =========================
        // PricingRules CRUD
        // =========================
        public async Task<Guid> CreateRuleAsync(CreatePricingRuleDto dto, CancellationToken ct = default)
        {
            if (dto.ServiceId == Guid.Empty) throw new BadRequestAppException("Invalid service id.");

            var servicesRepo = _unitOfWork.Repo<ServiceCatalogItem>();
            var rulesRepo = _unitOfWork.Repo<PricingRule>();

            var exists = await servicesRepo.AnyAsync(x => x.Id == dto.ServiceId, ct);
            if (!exists) throw new NotFoundAppException("Service not found.");

            var r = dto.Rule ?? throw new BadRequestAppException("Missing rule data.");

            var rule = new PricingRule
            {
                ServiceId = dto.ServiceId,
                Name = (r.Name ?? "").Trim(),
                Priority = r.Priority,
                ConditionExpr = string.IsNullOrWhiteSpace(r.ConditionExpr) ? "true" : r.ConditionExpr.Trim(),
                Action = r.Action,
                ValueExpr = string.IsNullOrWhiteSpace(r.ValueExpr) ? "0" : r.ValueExpr.Trim(),
                Label = string.IsNullOrWhiteSpace(r.Label) ? null : r.Label.Trim(),
                Scope = string.IsNullOrWhiteSpace(r.Scope) ? "LINE_ITEM" : r.Scope.Trim(),
                IsActive = r.IsActive,
                ValidFrom = r.ValidFrom,
                ValidTo = r.ValidTo
            };

            await rulesRepo.AddAsync(rule, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterServiceChangeAsync(dto.ServiceId, ct);

            _log.LogInformation("Pricing rule created. {ServiceId} {RuleId}", dto.ServiceId, rule.Id);
            return rule.Id;
        }

        public async Task UpdateRuleAsync(UpdatePricingRuleDto dto, CancellationToken ct = default)
        {
            if (dto.ServiceId == Guid.Empty) throw new BadRequestAppException("Invalid service id.");
            if (dto.RuleId == Guid.Empty) throw new BadRequestAppException("Invalid rule id.");

            var rulesRepo = _unitOfWork.Repo<PricingRule>();

            var rule = await rulesRepo.FirstOrDefaultAsync(
                x => x.Id == dto.RuleId && x.ServiceId == dto.ServiceId,
                ct,
                asNoTracking: false);

            if (rule is null) throw new NotFoundAppException("Rule not found.");

            var r = dto.Rule ?? throw new BadRequestAppException("Missing rule data.");

            rule.Name = (r.Name ?? "").Trim();
            rule.Priority = r.Priority;
            rule.ConditionExpr = string.IsNullOrWhiteSpace(r.ConditionExpr) ? "true" : r.ConditionExpr.Trim();
            rule.Action = r.Action;
            rule.ValueExpr = string.IsNullOrWhiteSpace(r.ValueExpr) ? "0" : r.ValueExpr.Trim();
            rule.Label = string.IsNullOrWhiteSpace(r.Label) ? null : r.Label.Trim();
            rule.Scope = string.IsNullOrWhiteSpace(r.Scope) ? "LINE_ITEM" : r.Scope.Trim();
            rule.IsActive = r.IsActive;
            rule.ValidFrom = r.ValidFrom;
            rule.ValidTo = r.ValidTo;

            rulesRepo.Update(rule);
            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterServiceChangeAsync(dto.ServiceId, ct);

            _log.LogInformation("Pricing rule updated. {ServiceId} {RuleId}", dto.ServiceId, dto.RuleId);
        }

        public async Task DeleteRuleAsync(DeletePricingRuleDto dto, CancellationToken ct = default)
        {
            if (dto.ServiceId == Guid.Empty) throw new BadRequestAppException("Invalid service id.");
            if (dto.RuleId == Guid.Empty) throw new BadRequestAppException("Invalid rule id.");

            var rulesRepo = _unitOfWork.Repo<PricingRule>();

            var rule = await rulesRepo.FirstOrDefaultAsync(
                x => x.Id == dto.RuleId && x.ServiceId == dto.ServiceId,
                ct,
                asNoTracking: false);

            if (rule is null) return;

            rulesRepo.Remove(rule);
            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterServiceChangeAsync(dto.ServiceId, ct);

            _log.LogInformation("Pricing rule deleted. {ServiceId} {RuleId}", dto.ServiceId, dto.RuleId);
        }

        // =========================
        // Helpers
        // =========================
        private static JsonDocument? ParseJsonOrNull(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            // validator موجود، بس خلّينا safe:
            try { return JsonDocument.Parse(json); }
            catch { throw new BadRequestAppException("Invalid JSON for ConfigSchemaJson."); }
        }

        private async Task InvalidateAfterServiceChangeAsync(Guid serviceId, CancellationToken ct)
        {
            await _cache.RemoveAsync(ServiceCacheKeys.Details(serviceId), ct);
            await _cache.BumpVersionAsync(ServiceCacheKeys.ListVersionKey, ct);
        }
    }
}
