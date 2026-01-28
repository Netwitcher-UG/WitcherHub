using System;
using System.Collections.Generic;
using System.Text;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Models.DTO.Project;
using WitcherHub.Application.Models.DTO.Services;
using WitcherHub.Application.Models.View.Project;
using WitcherHub.Application.Models.View.Services;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Interfaces.ManageData
{
    public interface IProject
    {
        Task<PagedResult<ProjectViews.ProjectListItemView>> GetProjectsAsync(
              int page = 1,
              int pageSize = 10,
              string? search = null,
              string? customerName = null,
              ProjectStatus? status = null,
              CancellationToken ct = default);

        Task<ProjectViews.ProjectDetailsView?> GetProjectAsync(Guid id, CancellationToken ct = default);

        Task<Guid> CreateAsync(CreateProjectDto dto, Guid? createdById = null, CancellationToken ct = default);
        Task UpdateAsync(Guid id, UpdateProjectDto dto, CancellationToken ct = default);
        Task ChangeStatusAsync(Guid id, ProjectStatus status, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
