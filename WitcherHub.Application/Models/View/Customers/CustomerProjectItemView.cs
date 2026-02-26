using System;
using System.Collections.Generic;
using System.Text;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.View.Customers
{
    public sealed class CustomerProjectItemView
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public ProjectStatus? Status { get; set; }
        public DateOnly? StartDate { get; set; }  // "yyyy-MM-dd"
        public DateOnly? EndDate { get; set; }    // "yyyy-MM-dd"
    }
}
