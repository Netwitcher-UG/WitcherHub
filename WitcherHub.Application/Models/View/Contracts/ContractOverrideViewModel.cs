using System;
using System.Collections.Generic;

namespace WitcherHub.Application.Models.View.Contracts
{
    public class ContractOverrideViewModel
    {
        public Guid ContractId { get; set; }
        public Guid ProjectId { get; set; }

        public string ContractNo { get; set; } = "";
        public string Currency { get; set; } = "EUR";

        public string ProjectTitle { get; set; } = "Project";
        public string CustomerName { get; set; } = "";
        public string CustomerEmail { get; set; } = "";

        public bool IsLocked { get; set; }

        public List<PositionVm> Positions { get; set; } = new();

        public class PositionVm
        {
            public int PositionNo { get; set; }
            public string Title { get; set; } = "";
            public decimal? LineNetPrice { get; set; }

            public SectionsVm Sections { get; set; } = new();

            public class SectionsVm
            {
                public string Scope { get; set; } = "";

                public List<string> Deliverables { get; set; } = new();
                public List<string> OutOfScope { get; set; } = new();
                public List<string> CustomerResponsibilities { get; set; } = new();
                public List<string> AcceptanceCriteria { get; set; } = new();

                public string Timeline { get; set; } = "";
                public string Assumptions { get; set; } = "";
                public string Revisions { get; set; } = "";
            }
        }
    }
}