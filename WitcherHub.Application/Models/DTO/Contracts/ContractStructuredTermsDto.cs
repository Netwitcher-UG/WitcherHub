using System;
using System.Collections.Generic;
using System.Text.Json;

namespace WitcherHub.Application.Models.DTO.Contracts
{
    public class ContractStructuredTermsDto
    {
        public string Version { get; set; } = "1.0";
        public string Language { get; set; } = "de-DE";

        // Anlage A variable part (editable)
        public List<ContractPositionSpecDto> Positions { get; set; } = new();

        // Optional: metadata for audit / regeneration
        public string? GeneratedBy { get; set; } // "openai"
        public DateTimeOffset? GeneratedAt { get; set; }
    }

    public class ContractPositionSpecDto
    {
        public int PositionNo { get; set; } = 1;

        // Link to your existing item if needed
        public Guid? ContractItemId { get; set; }
        public Guid? ServiceId { get; set; }

        public string Title { get; set; } = "";

        // Pricing (Netto)
        public decimal? Quantity { get; set; } = 1;
        public decimal? UnitNetPrice { get; set; } // optional
        public decimal? LineNetPrice { get; set; } // agreed net price (main)
        public decimal? TaxRatePercent { get; set; } // e.g. 19

        // Main structured sections
        public ContractPositionSectionsDto Sections { get; set; } = new();

        // ✅ Allow admin custom clauses/fields
        public List<ContractCustomClauseDto> CustomClauses { get; set; } = new();

        // Keep GPT raw if you want debugging
        public JsonDocument? AiRaw { get; set; }
    }

    public class ContractPositionSectionsDto
    {
        public string Scope { get; set; } = "";
        public List<string> Deliverables { get; set; } = new();
        public List<string> OutOfScope { get; set; } = new();
        public List<string> CustomerResponsibilities { get; set; } = new();
        public List<string> AcceptanceCriteria { get; set; } = new();

        // Optional common contract needs
        public string? Timeline { get; set; }
        public string? Assumptions { get; set; }
        public string? Revisions { get; set; }
    }

    public class ContractCustomClauseDto
    {
        public string Title { get; set; } = "";       // e.g. "Datenschutz"
        public string Body { get; set; } = "";        // markdown text
        public int Order { get; set; } = 1;
    }
}