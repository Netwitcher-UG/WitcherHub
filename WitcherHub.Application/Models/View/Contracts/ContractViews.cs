using System;
using System.Collections.Generic;
using System.Text.Json;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.View.Contracts
{
    public class ContractViews
    {
        public class ContractListItemView
        {
            public Guid Id { get; set; }
            public Guid ProjectId { get; set; }

            public string ContractNo { get; set; } = "";
            public DocumentStatus Status { get; set; }
            public string Currency { get; set; } = "EUR";

            public DateTimeOffset CreatedAt { get; set; }
            public DateOnly? StartDate { get; set; }
            public DateOnly? EndDate { get; set; }

            public decimal ItemsTotal { get; set; }
        }

        public class ContractDetailsView
        {
            public Guid Id { get; set; }
            public Guid ProjectId { get; set; }

            public string ContractNo { get; set; } = "";
            public DocumentStatus Status { get; set; }
            public string Currency { get; set; } = "EUR";

            public string? Terms { get; set; }
            public JsonDocument? TermsStructured { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateOnly? StartDate { get; set; }
            public DateOnly? EndDate { get; set; }

            public DateTimeOffset? SignedAt { get; set; }

            public List<ContractItemItemView> Items { get; set; } = new();
            public List<ContractSignatureView> Signatures { get; set; } = new();
        }

        public class ContractItemItemView
        {
            public Guid Id { get; set; }
            public Guid? ServiceId { get; set; }
            public string? ServiceName { get; set; }

            public string Title { get; set; } = "";
            public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");

            public decimal? AgreedPrice { get; set; }
            public int Position { get; set; }
        }

        public class ContractSignatureView
        {
            public Guid Id { get; set; }
            public string SignerName { get; set; } = "";
            public string? SignerEmail { get; set; }
            public DateTimeOffset? SignedAt { get; set; }
            public JsonDocument? SignatureData { get; set; }
        }
    }
}
