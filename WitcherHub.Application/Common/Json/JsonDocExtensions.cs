using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace WitcherHub.Application.Common.Json
{

    public static class JsonDocExtensions
    {
        private static readonly JsonSerializerOptions Opt = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static JsonDocument ToJsonDocument<T>(this T value)
            => JsonDocument.Parse(JsonSerializer.Serialize(value, Opt));

        public static T? FromJsonDocument<T>(this JsonDocument doc)
            => JsonSerializer.Deserialize<T>(doc.RootElement.GetRawText(), Opt);
    }
}
