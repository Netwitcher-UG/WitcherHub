using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WitcherHub.Application.Common.CacheKeys;
using WitcherHub.Application.Common.Caching;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Project;
using WitcherHub.Application.Models.View.Project;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.ManageData.Projects
{
    public sealed class ManageProject : IProject
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppCache _cache;
        private readonly ILogger<ManageProject> _log;

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

        public ManageProject(IUnitOfWork unitOfWork, IAppCache cache, ILogger<ManageProject> log)
        {
            _unitOfWork = unitOfWork;
            _cache = cache;
            _log = log;
        }

        // =========================
        // Listing (Pagination + Search)
        // =========================
        public async Task<PagedResult<ProjectViews.ProjectListItemView>> GetProjectsAsync(
            int page = 1,
            int pageSize = 10,
            string? search = null,
            string? customerName = null,
            ProjectStatus? status = null,
            CancellationToken ct = default)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 200 ? 10 : pageSize;

            var version = await _cache.GetOrCreateVersionAsync(ProjectCacheKeys.ListVersionKey, ct);
            var cacheKey = ProjectCacheKeys.ListWithVersion(page, pageSize, search, customerName, status?.ToString(), version);

            return await _cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    var repo = _unitOfWork.Repo<Project>();
                    var q = repo.Query(asNoTracking: true);

                    // Filter by customer name
                    if (!string.IsNullOrWhiteSpace(customerName))
                    {
                        var cs = customerName.Trim();
                        var escapedCs = EscapeLike(cs);
                        var customerPattern = $"%{escapedCs}%";

                        q = q.Where(x => EF.Functions.Like(x.Customer.Name, customerPattern, "!"));
                    }

                    // Filter by status
                    if (status.HasValue)
                        q = q.Where(x => x.Status == status.Value);

                    // General search
                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        var s = search.Trim();
                        var escaped = EscapeLike(s);
                        var pattern = $"%{escaped}%";

                        q = q.Where(x =>
                            EF.Functions.Like(x.Title, pattern, "!") ||
                            (x.Description != null && EF.Functions.Like(x.Description, pattern, "!")) ||
                            EF.Functions.Like(x.Customer.Name, pattern, "!") ||
                            x.Customer.EmailAddresses.Any(e => EF.Functions.Like(e.Email, pattern, "!")));
                    }

                    var total = await q.LongCountAsync(token);
                    if (total == 0)
                        return PagedResult<ProjectViews.ProjectListItemView>.Empty(page, pageSize);

                    var items = await q
                        .OrderByDescending(x => x.CreatedAt)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .Select(x => new ProjectViews.ProjectListItemView
                        {
                            Id = x.Id,
                            CustomerId = x.CustomerId,
                            Title = x.Title,
                            Status = x.Status,
                            StartDate = x.StartDate,
                            EndDate = x.EndDate,

                            CustomerName = x.Customer.Name,

                            // أول إيميل موجود (لأنه ما عندك IsPrimary)
                            CustomerEmail = x.Customer.EmailAddresses
                                .OrderBy(e => e.Kind) // optional ترتيب ثابت
                                .Select(e => e.Email)
                                .FirstOrDefault(),

                            QuotesCount = x.Quotes.Count,
                            ContractsCount = x.Contracts.Count,
                            InvoicesCount = x.Invoices.Count,
                            MilestonesCount = x.Milestones.Count,

                            // BaseEntity عندك DateTimeOffset
                            CreatedAt = x.CreatedAt,
                            UpdatedAt = x.UpdatedAt
                        })
                        .ToListAsync(token);

                    return new PagedResult<ProjectViews.ProjectListItemView>
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
        public async Task<ProjectViews.ProjectDetailsView?> GetProjectAsync(Guid id, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid project id.");

            var cacheKey = ProjectCacheKeys.Details(id);

            return await _cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    var repo = _unitOfWork.Repo<Project>();

                    var p = await repo.Query(asNoTracking: true)
                        .Where(x => x.Id == id)
                        .Select(x => new ProjectViews.ProjectDetailsView
                        {
                            Id = x.Id,
                            CustomerId = x.CustomerId,
                            Title = x.Title,
                            Description = x.Description,
                            Status = x.Status,
                            StartDate = x.StartDate,
                            EndDate = x.EndDate,

                            Customer = new ProjectViews.CustomerMiniView
                            {
                                Id = x.Customer.Id,
                                Name = x.Customer.Name,
                                Email = x.Customer.EmailAddresses
                                    .OrderBy(e => e.Kind)
                                    .Select(e => e.Email)
                                    .FirstOrDefault()
                            },

                            CreatedBy = x.CreatedBy == null ? null : new ProjectViews.UserMiniView
                            {
                                Id = x.CreatedBy.Id,
                                DisplayName = x.CreatedBy.UserName ?? "",
                                Email = x.CreatedBy.Email
                            },

                            QuotesCount = x.Quotes.Count,
                            ContractsCount = x.Contracts.Count,
                            InvoicesCount = x.Invoices.Count,
                            MilestonesCount = x.Milestones.Count,

                            CreatedAt = x.CreatedAt,
                            UpdatedAt = x.UpdatedAt
                        })
                        .FirstOrDefaultAsync(token);

                    return p;
                },
                DetailsCacheOptions,
                ct);
        }

        // =========================
        // CRUD
        // =========================
        public async Task<Guid> CreateAsync(CreateProjectDto dto, Guid? createdById = null, CancellationToken ct = default)
        {
            if (dto is null) throw new BadRequestAppException("Invalid payload.");
            if (dto.CustomerId == Guid.Empty) throw new BadRequestAppException("Invalid customer id.");
            if (string.IsNullOrWhiteSpace(dto.Title)) throw new BadRequestAppException("Title is required.");

            if (dto.EndDate.HasValue && dto.StartDate.HasValue && dto.EndDate < dto.StartDate)
                throw new BadRequestAppException("EndDate cannot be before StartDate.");

            var customersRepo = _unitOfWork.Repo<Customer>();
            var exists = await customersRepo.AnyAsync(x => x.Id == dto.CustomerId, ct);
            if (!exists) throw new NotFoundAppException("Customer not found.");

            var projectsRepo = _unitOfWork.Repo<Project>();

            var project = new Project
            {
                CustomerId = dto.CustomerId,
                Title = dto.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                Status = ProjectStatus.Draft,
                CreatedById = createdById
            };

            await projectsRepo.AddAsync(project, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterProjectChangeAsync(project.Id, ct);

            _log.LogInformation("Project created. {ProjectId}", project.Id);
            return project.Id;
        }

        public async Task UpdateAsync(Guid id, UpdateProjectDto dto, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid project id.");
            if (dto is null) throw new BadRequestAppException("Invalid payload.");

            if (dto.EndDate.HasValue && dto.StartDate.HasValue && dto.EndDate < dto.StartDate)
                throw new BadRequestAppException("EndDate cannot be before StartDate.");

            var repo = _unitOfWork.Repo<Project>();
            var project = await repo.GetByIdAsync(id, ct: ct, asNoTracking: false);
            if (project is null) throw new NotFoundAppException("Project not found.");

            if (project.Status == ProjectStatus.Closed)
                throw new BadRequestAppException("Closed project cannot be edited.");

            if (dto.Title is not null)
            {
                var t = dto.Title.Trim();
                if (string.IsNullOrWhiteSpace(t)) throw new BadRequestAppException("Title cannot be empty.");
                project.Title = t;
            }

            if (dto.Description is not null)
                project.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();

            if (dto.StartDate.HasValue) project.StartDate = dto.StartDate;
            if (dto.EndDate.HasValue) project.EndDate = dto.EndDate;

            await _unitOfWork.SaveChangesAsync(ct);
            await InvalidateAfterProjectChangeAsync(id, ct);

            _log.LogInformation("Project updated. {ProjectId}", id);
        }

        public async Task ChangeStatusAsync(Guid id, ProjectStatus status, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid project id.");

            var repo = _unitOfWork.Repo<Project>();
            var project = await repo.GetByIdAsync(id, ct: ct, asNoTracking: false);
            if (project is null) throw new NotFoundAppException("Project not found.");

            var from = project.Status;
            if (!IsValidTransition(from, status))
                throw new BadRequestAppException($"Invalid status transition: {from} -> {status}.");

            project.Status = status;

            await _unitOfWork.SaveChangesAsync(ct);
            await InvalidateAfterProjectChangeAsync(id, ct);

            _log.LogInformation("Project status changed. {ProjectId} {From} -> {To}", id, from, status);

            static bool IsValidTransition(ProjectStatus from, ProjectStatus to) => (from, to) switch
            {
                (ProjectStatus.Draft, ProjectStatus.Active) => true,
                (ProjectStatus.Active, ProjectStatus.Closed) => true,

                (ProjectStatus.Draft, ProjectStatus.Draft) => true,
                (ProjectStatus.Active, ProjectStatus.Active) => true,
                (ProjectStatus.Closed, ProjectStatus.Closed) => true,

                _ => false
            };
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            if (id == Guid.Empty) throw new BadRequestAppException("Invalid project id.");

            var repo = _unitOfWork.Repo<Project>();
            var project = await repo.GetByIdAsync(id, ct: ct, asNoTracking: false,
                x => x.Quotes, x => x.Contracts, x => x.Invoices, x => x.Milestones);

            if (project is null) return;

            if (project.Status != ProjectStatus.Draft)
                throw new BadRequestAppException("Only draft projects can be deleted.");

            if (project.Quotes.Count > 0 || project.Contracts.Count > 0 || project.Invoices.Count > 0 || project.Milestones.Count > 0)
                throw new BadRequestAppException("Project cannot be deleted because it has related data.");

            repo.Remove(project);
            await _unitOfWork.SaveChangesAsync(ct);

            await InvalidateAfterProjectChangeAsync(id, ct);

            _log.LogInformation("Project deleted. {ProjectId}", id);
        }

        private async Task InvalidateAfterProjectChangeAsync(Guid projectId, CancellationToken ct)
        {
            await _cache.RemoveAsync(ProjectCacheKeys.Details(projectId), ct);
            await _cache.BumpVersionAsync(ProjectCacheKeys.ListVersionKey, ct);
        }
    }
}
