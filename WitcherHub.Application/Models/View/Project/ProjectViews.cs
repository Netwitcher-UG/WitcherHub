using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Domain.Projects;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.View.Project
{
    public class ProjectViews
    {
        public class ProjectListItemView
        {
            public Guid Id { get; set; }
            public string Title { get; set; } = "";

            public ProjectStatus Status { get; set; }

            public DateOnly? StartDate { get; set; }
            public DateOnly? EndDate { get; set; }

            // Customer snapshot for list UI (بدون ما تعمل extra call)
            public Guid CustomerId { get; set; }
            public string CustomerName { get; set; } = "";
            public string? CustomerEmail { get; set; }

            // Useful counters (computed in query)
            /// <summary>
            /// Archived projects are hidden from the active list and kept in
            /// full. Separate from Status, which stays exactly as it was so
            /// restoring returns the project unchanged.
            /// </summary>
            public DateTimeOffset? ArchivedAt { get; set; }

            public bool IsArchived => ArchivedAt.HasValue;

            /// <summary>
            /// How far the documents have got, as facts about the documents. The
            /// list used to show only the project's Status, which a contract had
            /// been writing — so the one column had to mean two things and could
            /// not honestly mean either.
            /// </summary>
            public DocumentProgress QuoteProgress { get; set; }
            public DocumentProgress ContractProgress { get; set; }
            public DocumentProgress InvoiceProgress { get; set; }

            public int QuotesCount { get; set; }
            public int ContractsCount { get; set; }
            public int InvoicesCount { get; set; }
            public int MilestonesCount { get; set; }

            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset? UpdatedAt { get; set; }
        }

        public class ProjectDetailsView
        {
            public Guid Id { get; set; }
            public string Title { get; set; } = "";
            public string? Description { get; set; }

            public ProjectStatus Status { get; set; }

            public DateOnly? StartDate { get; set; }
            public DateOnly? EndDate { get; set; }

            public CustomerMiniView Customer { get; set; } = new();
            public Guid CustomerId { get; set; }


            public UserMiniView? CreatedBy { get; set; }

            // Summary numbers for UI tabs
            public int QuotesCount { get; set; }
            public int ContractsCount { get; set; }
            public int InvoicesCount { get; set; }
            public int MilestonesCount { get; set; }

            // optional: "آخر شي صار" (مفيد لاحقاً لما تدخل Quote/Invoice)
            public DateTime? LastActivityAt { get; set; }

            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset? UpdatedAt { get; set; }
        }
        public class CustomerMiniView
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public string? Email { get; set; }
        }

        public class UserMiniView
        {
            public Guid Id { get; set; }
            public string DisplayName { get; set; } = "";
            public string? Email { get; set; }
        }
    }
}
