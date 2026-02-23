using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WitcherHub.Infrastructure.Services.Lexware
{
    /// <summary>
    /// Lexware expects date-time strings with milliseconds precision (3 digits) e.g. 2026-02-22T14:47:04.528+01:00
    /// .NET default serialization may output 7 fractional digits which Lexware rejects.
    /// </summary>
    public sealed class LexwareDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffzzz";

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Expected string for DateTimeOffset, got {reader.TokenType}.");

            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s))
                return default;

            // Accept Lexware and general ISO formats
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                return dto;

            if (DateTimeOffset.TryParseExact(s, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out dto))
                return dto;

            throw new JsonException($"Invalid DateTimeOffset value: '{s}'.");
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
        }
    }

    public sealed class LexwareNullableDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
    {
        private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffzzz";

        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Expected string/null for DateTimeOffset?, got {reader.TokenType}.");

            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s))
                return null;

            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                return dto;

            if (DateTimeOffset.TryParseExact(s, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out dto))
                return dto;

            throw new JsonException($"Invalid DateTimeOffset? value: '{s}'.");
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
        {
            if (value is null) { writer.WriteNullValue(); return; }
            writer.WriteStringValue(value.Value.ToString(Format, CultureInfo.InvariantCulture));
        }
    }
}
