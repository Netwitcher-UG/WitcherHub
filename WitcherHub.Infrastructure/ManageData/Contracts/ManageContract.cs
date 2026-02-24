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

                var item = new ContractItem
                {
                    ContractId = dto.ContractId,
                    Title = (dto.Item.Title ?? "").Trim(),
                    ServiceId = dto.Item.ServiceId,
                    Config = dto.Item.Config ?? JsonDocument.Parse("{}"),
                    AgreedPrice = dto.Item.AgreedPrice,
                    Position = dto.Item.Position > 0 ? dto.Item.Position : (maxPos + 1)
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

        // =========================
        // ITEMS: UPDATE
        // =========================
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
                item.AgreedPrice = dto.Item.AgreedPrice;

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

        
    }
}
