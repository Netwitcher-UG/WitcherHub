using System;
using System.Collections.Generic;
using System.Text;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Models.DTO.Project;
using WitcherHub.Application.Models.DTO.Services;
using WitcherHub.Application.Models.View.Project;
using WitcherHub.Application.Models.View.Services;
using static WitcherHub.Infrastructure.Data.Models.Enums;

using WitcherHub.Domain.Projects;

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
        /// <summary>
        /// What deleting this project permanently would destroy, and whether it
        /// is allowed at all.
        ///
        /// Asked before the confirmation is shown, so a person sees what they are
        /// agreeing to rather than discovering it afterwards.
        /// </summary>
        Task<ProjectDeletionImpact> GetDeletionImpactAsync(Guid id, CancellationToken ct = default);

        /// <summary>
        /// Deletes a project permanently.
        ///
        /// Refused whenever the project holds a financial or legal record. The
        /// customer is never touched — a project is one piece of work for a
        /// customer, not the customer.
        /// </summary>
        Task DeleteAsync(Guid id, CancellationToken ct = default);

        /// <summary>
        /// Takes a project out of the active list without destroying anything.
        /// The normal way to finish with a project.
        /// </summary>
        Task ArchiveAsync(Guid id, Guid? archivedById = null, CancellationToken ct = default);

        /// <summary>Puts an archived project back into the active list.</summary>
        Task RestoreAsync(Guid id, CancellationToken ct = default);

        /// <summary>
        /// The project's own status alongside the state of its documents, as one
        /// value read from one place — so no two screens can disagree.
        /// </summary>
        Task<ProjectWorkflowState> GetWorkflowStateAsync(Guid id, CancellationToken ct = default);
    }
}
