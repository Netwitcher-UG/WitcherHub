using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using WitcherHub.Application.Interfaces.ManageData;
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
