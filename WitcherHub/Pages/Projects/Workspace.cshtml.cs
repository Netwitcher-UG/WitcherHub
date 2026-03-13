using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Interfaces.ManageData;

namespace WitcherHub.Pages.Projects
{
    public class WorkspaceModel : PageModel
    {
        private readonly IProject _projects;

        public WorkspaceModel(IProject projects)
        {
            _projects = projects;
        }

        [BindProperty(SupportsGet = true, Name = "id")]
        public Guid ProjectId { get; set; }

        [BindProperty(SupportsGet = true, Name = "tab")]
        public string? Tab { get; set; }

        public string ProjectTitle { get; private set; } = "Project Workspace";

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (ProjectId == Guid.Empty)
                return RedirectToPage("/Projects");

            Tab = NormalizeTab(Tab);

            var project = await _projects.GetProjectAsync(ProjectId, ct);
            if (project is null)
                return NotFound();

            ProjectTitle = string.IsNullOrWhiteSpace(project.Title) ? "Project Workspace" : project.Title;
            return Page();
        }

        private static string NormalizeTab(string? tab)
        {
            return (tab ?? "").Trim().ToLowerInvariant() switch
            {
                "quotes" => "quotes",
                "invoices" => "invoices",
                "contracts" => "contracts",
                _ => "overview"
            };
        }
    }
}