using System;
using System.Text.Json.Serialization;

namespace WitcherHub.Infrastructure.Services.Lexware
{
    public sealed class LexwareWebhookPayload
    {
        [JsonPropertyName("organizationId")]
        public string? OrganizationId { get; set; }

        [JsonPropertyName("eventType")]
        public string? EventType { get; set; }

        [JsonPropertyName("resourceId")]
        public string? ResourceId { get; set; }

        [JsonPropertyName("eventDate")]
        public DateTimeOffset? EventDate { get; set; }
    }
}
